/*
 * Siloé Stream — caméra HDMI par USB (UVC) pour MyCam.
 *
 * Une carte de capture HDMI (Elgato Cam Link, cartes « USB 3.0 HDMI »…) branchée au téléphone est une caméra UVC.
 * Android ne la donne pas toujours à l'application comme caméra : on lui parle donc directement, par le descripteur
 * de fichier usbfs que donne UsbDeviceConnection. Ce fichier fait la partie qui doit aller vite :
 *   - les transferts isochrones ou « bulk » (URB usbfs) et le réassemblage des images UVC (en-têtes FID / EOF) ;
 *   - les conversions YUYV, NV12 ou RGBA → plans de l'encodeur H.264 (I420, NV12, NV21), NV21 (JPEG) et aperçu RGBA.
 * Le choix du format, la négociation UVC (probe / commit) et l'encodage restent en Kotlin (CameraUsb.kt).
 *
 * Licence : GNU GPL version 2 ou ultérieure, comme Siloé Stream.
 */
#include <errno.h>
#ifndef SILOE_SANS_JNI
#include <jni.h>
#endif
#include <linux/usbdevice_fs.h>
#include <pthread.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <time.h>

#ifdef __ANDROID__
#include <android/log.h>
#define JOURNAL(...) __android_log_print(ANDROID_LOG_INFO, "SiloeUvc", __VA_ARGS__)
#else
#include <stdio.h>
#define JOURNAL(...) (fprintf(stderr, __VA_ARGS__), fputc('\n', stderr))
#endif

/* ------------------------------------------------------------------ formats */
#define FORMAT_YUYV 1
#define FORMAT_NV12 2
#define FORMAT_RGBA 3
#define FORMAT_MJPEG 4

/* ------------------------------------------------------------------ flux UVC */

typedef struct {
    int fd;
    int point;        /* adresse du point d'accès (0x81…) */
    int iso;          /* 1 : isochrone, 0 : bulk */
    int paquet;       /* iso : octets par paquet (bande passante de l'alternative) */
    int paquetsParUrb;
    int tailleUrb;
    int transfert;    /* dwMaxPayloadTransferSize négocié */
    int nUrb;
    int mjpeg;
    int attendu;      /* octets d'une image non compressée (0 : pas de contrôle) */
    int maxImage;

    struct usbdevfs_urb **urbs;
    uint8_t **tampons;
    pthread_t fil;
    int filLance;
    pthread_mutex_t verrouUrb; /* « arrêt demandé » et « renvoi d'un URB » ne se croisent pas */
    volatile int arret;
    int enVol;

    /* image en cours d'assemblage (fil des URB seulement) */
    uint8_t *encours;
    int nEncours;
    int fid;
    int mauvaise;
    int64_t debutImage;
    int resteBulk;    /* bulk : octets encore attendus du transfert en cours (suite sans en-tête) */
    int eofAttente;   /* bulk : l'en-tête disait « fin d'image », appliquée à la fin du transfert */

    /* dernière image complète, pour le fil Kotlin */
    pthread_mutex_t verrou;
    pthread_cond_t signal;
    uint8_t *prete;
    int nPrete;
    int64_t tPrete;
    uint32_t seq, seqLue;
    int fini;
    int erreur;
    uint32_t images, rejets;
} Flux;

static int64_t maintenantNs(void) {
    struct timespec t;
#ifdef CLOCK_BOOTTIME
    clock_gettime(CLOCK_BOOTTIME, &t); /* = SystemClock.elapsedRealtimeNanos() */
#else
    clock_gettime(CLOCK_MONOTONIC, &t);
#endif
    return (int64_t)t.tv_sec * 1000000000LL + t.tv_nsec;
}

static void terminerImage(Flux *f) {
    int ok = !f->mauvaise && f->nEncours > 0;
    if (ok && f->mjpeg) ok = f->nEncours >= 4 && f->encours[0] == 0xFF && f->encours[1] == 0xD8;
    if (ok && !f->mjpeg && f->attendu > 0) ok = f->nEncours >= f->attendu;
    if (ok) {
        pthread_mutex_lock(&f->verrou);
        uint8_t *t = f->prete;
        f->prete = f->encours;
        f->encours = t;
        f->nPrete = f->nEncours;
        f->tPrete = f->debutImage;
        f->seq++;
        f->images++;
        pthread_cond_broadcast(&f->signal);
        pthread_mutex_unlock(&f->verrou);
    } else if (f->nEncours > 0 || f->mauvaise) {
        f->rejets++;
    }
    f->nEncours = 0;
    f->mauvaise = 0;
}

static void ajouter(Flux *f, const uint8_t *p, int n) {
    if (n <= 0) return;
    if (f->nEncours == 0) f->debutImage = maintenantNs();
    if (f->nEncours + n > f->maxImage) {
        f->mauvaise = 1; /* image trop grande : flux décalé, on attend la suivante */
        return;
    }
    memcpy(f->encours + f->nEncours, p, n);
    f->nEncours += n;
}

/** Une charge utile UVC : en-tête (longueur, drapeaux FID / EOF / ERR), puis les données. */
static void chargeUtile(Flux *f, const uint8_t *p, int n, int finTransfert) {
    if (n < 2) return;
    int hle = p[0];
    if (hle < 2 || hle > n) return;
    int bfh = p[1];
    int fid = bfh & 1;
    if (f->nEncours > 0 && fid != f->fid) terminerImage(f); /* l'image précédente n'avait pas d'EOF */
    f->fid = fid;
    if (bfh & 0x40) f->mauvaise = 1;
    ajouter(f, p + hle, n - hle);
    if (bfh & 0x02) {
        if (finTransfert) terminerImage(f);
        else f->eofAttente = 1;
    }
}

static void traiterUrb(Flux *f, struct usbdevfs_urb *u) {
    uint8_t *b = (uint8_t *)u->buffer;
    if (f->iso) {
        int pos = 0;
        for (int i = 0; i < u->number_of_packets; i++) {
            struct usbdevfs_iso_packet_desc *d = &u->iso_frame_desc[i];
            if (d->status == 0 && d->actual_length > 0) chargeUtile(f, b + pos, (int)d->actual_length, 1);
            pos += d->length;
        }
        return;
    }
    int n = u->actual_length;
    if (u->status != 0 || n <= 0) {
        f->resteBulk = 0;
        if (f->eofAttente) {
            f->eofAttente = 0;
            terminerImage(f);
        }
        return;
    }
    if (f->resteBulk > 0) {
        /* suite d'un transfert plus grand qu'un URB : pas d'en-tête */
        ajouter(f, b, n);
        f->resteBulk = n < f->tailleUrb ? 0 : f->resteBulk - n;
        if (f->resteBulk < 0) f->resteBulk = 0;
        if (f->resteBulk == 0 && f->eofAttente) {
            f->eofAttente = 0;
            terminerImage(f);
        }
        return;
    }
    f->resteBulk = (n == f->tailleUrb && f->transfert > n) ? f->transfert - n : 0;
    chargeUtile(f, b, n, f->resteBulk == 0);
}

static void preparerUrb(Flux *f, struct usbdevfs_urb *u, uint8_t *tampon) {
    int paquets = f->iso ? f->paquetsParUrb : 0;
    memset(u, 0, sizeof(*u) + (size_t)paquets * sizeof(struct usbdevfs_iso_packet_desc));
    u->endpoint = (unsigned char)f->point;
    u->buffer = tampon;
    u->buffer_length = f->tailleUrb;
    if (f->iso) {
        u->type = USBDEVFS_URB_TYPE_ISO;
        u->flags = USBDEVFS_URB_ISO_ASAP;
        u->number_of_packets = paquets;
        for (int i = 0; i < paquets; i++) u->iso_frame_desc[i].length = f->paquet;
    } else {
        u->type = USBDEVFS_URB_TYPE_BULK;
    }
}

static void *boucle(void *a) {
    Flux *f = (Flux *)a;
    for (;;) {
        struct usbdevfs_urb *u = NULL;
        if (ioctl(f->fd, USBDEVFS_REAPURB, &u) < 0) {
            if (errno == EINTR) continue;
            f->erreur = errno;
            break;
        }
        if (u == NULL) continue;
        pthread_mutex_lock(&f->verrouUrb);
        int arret = f->arret;
        pthread_mutex_unlock(&f->verrouUrb);
        if (!arret && (u->status == 0 || f->iso)) traiterUrb(f, u);
        pthread_mutex_lock(&f->verrouUrb);
        if (f->arret) {
            f->enVol--;
            int fin = f->enVol <= 0;
            pthread_mutex_unlock(&f->verrouUrb);
            if (fin) break;
            continue;
        }
        int k = 0;
        while (k < f->nUrb && f->urbs[k] != u) k++;
        int r = -1;
        if (k < f->nUrb) {
            preparerUrb(f, u, f->tampons[k]);
            r = ioctl(f->fd, USBDEVFS_SUBMITURB, u);
        }
        if (r < 0) {
            int e = errno;
            f->enVol--;
            if (e == ENODEV || e == ESHUTDOWN || f->enVol <= 0) {
                f->erreur = e;
                pthread_mutex_unlock(&f->verrouUrb);
                if (f->enVol <= 0) break;
                continue;
            }
        }
        pthread_mutex_unlock(&f->verrouUrb);
    }
    pthread_mutex_lock(&f->verrou);
    f->fini = 1;
    pthread_cond_broadcast(&f->signal);
    pthread_mutex_unlock(&f->verrou);
    return NULL;
}

static void liberer(Flux *f) {
    if (!f) return;
    if (f->urbs) {
        for (int i = 0; i < f->nUrb; i++) free(f->urbs[i]);
        free(f->urbs);
    }
    if (f->tampons) {
        for (int i = 0; i < f->nUrb; i++) free(f->tampons[i]);
        free(f->tampons);
    }
    free(f->encours);
    free(f->prete);
    pthread_mutex_destroy(&f->verrou);
    pthread_mutex_destroy(&f->verrouUrb);
    pthread_cond_destroy(&f->signal);
    free(f);
}

static void arreterFlux(Flux *f) {
    if (!f) return;
    pthread_mutex_lock(&f->verrouUrb);
    f->arret = 1;
    for (int i = 0; i < f->nUrb; i++) {
        if (f->urbs[i]) ioctl(f->fd, USBDEVFS_DISCARDURB, f->urbs[i]);
    }
    pthread_mutex_unlock(&f->verrouUrb);
    /* les URB annulés reviennent au fil, qui s'arrête quand il n'en reste plus en vol (ou à la première erreur du périphérique) */
    if (f->filLance) pthread_join(f->fil, NULL);
    liberer(f);
}

static Flux *demarrerFlux(int fd, int point, int iso, int paquet, int transfert, int maxImage, int mjpeg, int attendu) {
    Flux *f = (Flux *)calloc(1, sizeof(Flux));
    if (!f) return NULL;
    pthread_mutex_init(&f->verrou, NULL);
    pthread_mutex_init(&f->verrouUrb, NULL);
    pthread_cond_init(&f->signal, NULL);
    f->fd = fd;
    f->point = point | 0x80;
    f->iso = iso;
    f->mjpeg = mjpeg;
    f->attendu = attendu;
    f->transfert = transfert > 0 ? transfert : 512 * 1024;
    f->maxImage = maxImage > 0 ? maxImage : 16 * 1024 * 1024;
    if (iso) {
        if (paquet <= 0) paquet = 3072;
        f->paquet = paquet;
        int n = (1024 * 1024) / paquet;
        if (n > 32) n = 32;
        if (n < 1) n = 1;
        f->paquetsParUrb = n;
        f->tailleUrb = n * paquet;
        f->nUrb = 8;
    } else {
        int t = f->transfert;
        if (t > 512 * 1024) t = 512 * 1024;
        if (t < 16 * 1024) t = 16 * 1024;
        t = (t + 1023) / 1024 * 1024;
        if (f->transfert < t && f->transfert >= 512) t = f->transfert; /* un URB = un transfert, quand il est petit */
        f->tailleUrb = t;
        f->nUrb = 8;
    }
    f->encours = (uint8_t *)malloc(f->maxImage);
    f->prete = (uint8_t *)malloc(f->maxImage);
    f->urbs = (struct usbdevfs_urb **)calloc(f->nUrb, sizeof(void *));
    f->tampons = (uint8_t **)calloc(f->nUrb, sizeof(void *));
    if (!f->encours || !f->prete || !f->urbs || !f->tampons) {
        liberer(f);
        return NULL;
    }
    size_t tailleDesc = sizeof(struct usbdevfs_urb) + (size_t)(iso ? f->paquetsParUrb : 0) * sizeof(struct usbdevfs_iso_packet_desc);
    for (int i = 0; i < f->nUrb; i++) {
        f->urbs[i] = (struct usbdevfs_urb *)calloc(1, tailleDesc);
        f->tampons[i] = (uint8_t *)malloc(f->tailleUrb);
        if (!f->urbs[i] || !f->tampons[i]) {
            liberer(f);
            return NULL;
        }
    }
    int soumis = 0;
    for (int i = 0; i < f->nUrb; i++) {
        preparerUrb(f, f->urbs[i], f->tampons[i]);
        if (ioctl(fd, USBDEVFS_SUBMITURB, f->urbs[i]) < 0) {
            int e = errno;
            JOURNAL("SUBMITURB %d refusé : errno %d", i, e);
            if (soumis == 0) {
                liberer(f);
                errno = e;
                return NULL;
            }
            break;
        }
        soumis++;
    }
    f->enVol = soumis;
    if (pthread_create(&f->fil, NULL, boucle, f) != 0) {
        f->filLance = 0;
        pthread_mutex_lock(&f->verrouUrb);
        f->arret = 1;
        for (int i = 0; i < f->nUrb; i++) ioctl(fd, USBDEVFS_DISCARDURB, f->urbs[i]);
        pthread_mutex_unlock(&f->verrouUrb);
        for (int i = 0; i < soumis; i++) {
            struct usbdevfs_urb *u = NULL;
            ioctl(fd, USBDEVFS_REAPURB, &u);
        }
        liberer(f);
        return NULL;
    }
    f->filLance = 1;
    JOURNAL("flux UVC : point 0x%02x, %s, %d URB de %d octets, transfert %d", f->point, iso ? "isochrone" : "bulk", f->nUrb, f->tailleUrb, f->transfert);
    return f;
}

/** La dernière image (attend au plus [ms]) : sa longueur, 0 si rien de neuf, -1 si le flux est fini (débranché). */
static int attendreImage(Flux *f, uint8_t *dst, int capacite, int ms, int64_t *temps) {
    pthread_mutex_lock(&f->verrou);
    if (f->seq == f->seqLue && !f->fini && ms > 0) {
        struct timespec fin;
        clock_gettime(CLOCK_REALTIME, &fin);
        fin.tv_sec += ms / 1000;
        fin.tv_nsec += (long)(ms % 1000) * 1000000L;
        if (fin.tv_nsec >= 1000000000L) {
            fin.tv_sec++;
            fin.tv_nsec -= 1000000000L;
        }
        while (f->seq == f->seqLue && !f->fini) {
            if (pthread_cond_timedwait(&f->signal, &f->verrou, &fin) == ETIMEDOUT) break;
        }
    }
    int n = 0;
    if (f->seq != f->seqLue) {
        n = f->nPrete < capacite ? f->nPrete : capacite;
        memcpy(dst, f->prete, n);
        if (temps) *temps = f->tPrete;
        f->seqLue = f->seq;
    } else if (f->fini) {
        n = -1;
    }
    pthread_mutex_unlock(&f->verrou);
    return n;
}

/* ------------------------------------------------------------------ couleurs */

/*
 * Matrices vers le YUV « plein écart BT.601 » que lit le PC (comme le JPEG et la caméra du téléphone) :
 *   0 : déjà plein écart BT.601 (copie) ; 1 : écart limité BT.709 (HDMI en HD : le cas des cartes de capture) ;
 *   2 : écart limité BT.601.
 * Y' = (Y−16)·255/219 + a·(U−128) + b·(V−128) ; U' = 128 + c·(U−128) + d·(V−128) ; V' = 128 + e·(U−128) + g·(V−128), en virgule fixe 16 bits.
 */
typedef struct {
    int id;
    int ky, a, b, c, d, e, g;
} Matrice;

static void matrice(int id, Matrice *m) {
    m->id = id;
    if (id == 0) {
        m->ky = 65536; m->a = 0; m->b = 0; m->c = 65536; m->d = 0; m->e = 0; m->g = 65536;
        return;
    }
    double kl = 255.0 / 219.0, kc = 255.0 / 224.0;
    double a = 0, b = 0, c = 1, d = 0, e = 0, g = 1;
    if (id == 1) {
        /* RGB du YCbCr BT.709, puis YCbCr BT.601 */
        a = 0.10159; b = 0.19610;
        c = 0.98985; d = -0.11067;
        e = -0.07246; g = 0.98338;
    }
    m->ky = (int)(kl * 65536 + 0.5);
    m->a = (int)(a * kc * 65536 + (a >= 0 ? 0.5 : -0.5));
    m->b = (int)(b * kc * 65536 + (b >= 0 ? 0.5 : -0.5));
    m->c = (int)(c * kc * 65536 + 0.5);
    m->d = (int)(d * kc * 65536 + (d >= 0 ? 0.5 : -0.5));
    m->e = (int)(e * kc * 65536 + (e >= 0 ? 0.5 : -0.5));
    m->g = (int)(g * kc * 65536 + 0.5);
}

static inline uint8_t borne(int v) { return (uint8_t)(v < 0 ? 0 : v > 255 ? 255 : v); }

static inline uint8_t luma(const Matrice *m, int y, int u, int v) {
    if (m->id == 0) return (uint8_t)y;
    return borne(((y - 16) * m->ky + (u - 128) * m->a + (v - 128) * m->b + 32768) >> 16);
}

static inline void chroma(const Matrice *m, int u, int v, uint8_t *uo, uint8_t *vo) {
    if (m->id == 0) {
        *uo = (uint8_t)u;
        *vo = (uint8_t)v;
        return;
    }
    int du = u - 128, dv = v - 128;
    *uo = borne(128 + ((du * m->c + dv * m->d + 32768) >> 16));
    *vo = borne(128 + ((du * m->e + dv * m->g + 32768) >> 16));
}

typedef struct {
    uint8_t *y, *u, *v;
    int yLigne, uLigne, vLigne, uPas, vPas;
    long yCap, uCap, vCap;
} Plans;

/**
 * Image source (YUYV, NV12 ou RGBA de w × h) → plans 4:2:0 de destination : la zone (x0, y0, cw × ch), coordonnées paires.
 * Rend 0, ou -1 si la zone ou les plans sont trop petits.
 */
static int convertir(int format, const uint8_t *src, long n, int w, int h, int x0, int y0, int cw, int ch, const Plans *p, int idMatrice) {
    x0 &= ~1; y0 &= ~1; cw &= ~1; ch &= ~1;
    if (cw <= 0 || ch <= 0 || x0 < 0 || y0 < 0 || x0 + cw > w || y0 + ch > h) return -1;
    long besoin = format == FORMAT_YUYV ? (long)w * h * 2 : format == FORMAT_NV12 ? (long)w * h * 3 / 2 : (long)w * h * 4;
    if (n < besoin) return -1;
    if ((long)(ch - 1) * p->yLigne + cw > p->yCap) return -1;
    if ((long)(ch / 2 - 1) * p->uLigne + (long)(cw / 2 - 1) * p->uPas + 1 > p->uCap) return -1;
    if ((long)(ch / 2 - 1) * p->vLigne + (long)(cw / 2 - 1) * p->vPas + 1 > p->vCap) return -1;
    Matrice m;
    matrice(format == FORMAT_RGBA ? 0 : idMatrice, &m);

    for (int r = 0; r < ch; r += 2) {
        uint8_t *dy0 = p->y + (long)r * p->yLigne;
        uint8_t *dy1 = dy0 + p->yLigne;
        uint8_t *du = p->u + (long)(r / 2) * p->uLigne;
        uint8_t *dv = p->v + (long)(r / 2) * p->vLigne;
        int sy = y0 + r;
        if (format == FORMAT_YUYV) {
            const uint8_t *s0 = src + ((long)sy * w + x0) * 2;
            const uint8_t *s1 = s0 + (long)w * 2;
            for (int c = 0; c < cw; c += 2) {
                int u0 = s0[1], v0 = s0[3], u1 = s1[1], v1 = s1[3];
                dy0[c] = luma(&m, s0[0], u0, v0);
                dy0[c + 1] = luma(&m, s0[2], u0, v0);
                dy1[c] = luma(&m, s1[0], u1, v1);
                dy1[c + 1] = luma(&m, s1[2], u1, v1);
                chroma(&m, (u0 + u1 + 1) >> 1, (v0 + v1 + 1) >> 1, du, dv);
                du += p->uPas;
                dv += p->vPas;
                s0 += 4;
                s1 += 4;
            }
        } else if (format == FORMAT_NV12) {
            const uint8_t *s0 = src + (long)sy * w + x0;
            const uint8_t *s1 = s0 + w;
            const uint8_t *suv = src + (long)w * h + (long)(sy / 2) * w + x0;
            for (int c = 0; c < cw; c += 2) {
                int u = suv[0], v = suv[1];
                dy0[c] = luma(&m, s0[c], u, v);
                dy0[c + 1] = luma(&m, s0[c + 1], u, v);
                dy1[c] = luma(&m, s1[c], u, v);
                dy1[c + 1] = luma(&m, s1[c + 1], u, v);
                chroma(&m, u, v, du, dv);
                du += p->uPas;
                dv += p->vPas;
                suv += 2;
            }
        } else {
            /* RGBA (Bitmap ARGB_8888 en mémoire) → YUV plein écart BT.601, comme le JPEG */
            const uint8_t *s0 = src + ((long)sy * w + x0) * 4;
            const uint8_t *s1 = s0 + (long)w * 4;
            for (int c = 0; c < cw; c += 2) {
                int rs = 0, gs = 0, bs = 0;
                const uint8_t *px[4] = {s0, s0 + 4, s1, s1 + 4};
                uint8_t *dst[4] = {dy0 + c, dy0 + c + 1, dy1 + c, dy1 + c + 1};
                for (int k = 0; k < 4; k++) {
                    int R = px[k][0], G = px[k][1], B = px[k][2];
                    *dst[k] = (uint8_t)((77 * R + 150 * G + 29 * B + 128) >> 8);
                    rs += R; gs += G; bs += B;
                }
                rs >>= 2; gs >>= 2; bs >>= 2;
                *du = borne(((-43 * rs - 85 * gs + 128 * bs + 128) >> 8) + 128);
                *dv = borne(((128 * rs - 107 * gs - 21 * bs + 128) >> 8) + 128);
                du += p->uPas;
                dv += p->vPas;
                s0 += 8;
                s1 += 8;
            }
        }
    }
    return 0;
}

/** Aperçu RGBA (pw × ph) d'une image YUYV ou NV12, au plus proche voisin. */
static int apercu(int format, const uint8_t *src, long n, int w, int h, uint8_t *dst, long cap, int pw, int ph, int idMatrice) {
    if (pw <= 0 || ph <= 0 || (long)pw * ph * 4 > cap) return -1;
    long besoin = format == FORMAT_YUYV ? (long)w * h * 2 : (long)w * h * 3 / 2;
    if (n < besoin) return -1;
    Matrice m;
    matrice(idMatrice, &m);
    for (int r = 0; r < ph; r++) {
        int sy = (int)((long)r * h / ph);
        uint8_t *o = dst + (long)r * pw * 4;
        for (int c = 0; c < pw; c++) {
            int sx = (int)((long)c * w / pw);
            int y, u, v;
            if (format == FORMAT_YUYV) {
                const uint8_t *s = src + ((long)sy * w + (sx & ~1)) * 2;
                y = (sx & 1) ? s[2] : s[0];
                u = s[1];
                v = s[3];
            } else {
                y = src[(long)sy * w + sx];
                const uint8_t *s = src + (long)w * h + (long)(sy / 2) * w + (sx & ~1);
                u = s[0];
                v = s[1];
            }
            uint8_t uo, vo;
            int yy = luma(&m, y, u, v);
            chroma(&m, u, v, &uo, &vo);
            int du = uo - 128, dv = vo - 128;
            o[0] = borne(yy + ((91881 * dv + 32768) >> 16));
            o[1] = borne(yy - ((22554 * du + 46802 * dv + 32768) >> 16));
            o[2] = borne(yy + ((116130 * du + 32768) >> 16));
            o[3] = 255;
            o += 4;
        }
    }
    return 0;
}

/* ------------------------------------------------------------------ JNI (ca.siloestream.app.UvcNatif) */
#ifndef SILOE_SANS_JNI

#define JNI(nom) Java_ca_siloestream_app_UvcNatif_##nom

JNIEXPORT jlong JNICALL JNI(demarrer)(JNIEnv *env, jclass cls, jint fd, jint point, jboolean iso, jint paquet, jint transfert, jint maxImage, jboolean mjpeg, jint attendu) {
    (void)env; (void)cls;
    Flux *f = demarrerFlux(fd, point, iso ? 1 : 0, paquet, transfert, maxImage, mjpeg ? 1 : 0, attendu);
    return f ? (jlong)(intptr_t)f : -(jlong)(errno > 0 ? errno : 1);
}

JNIEXPORT jint JNICALL JNI(attendre)(JNIEnv *env, jclass cls, jlong h, jobject dst, jint ms, jlongArray infos) {
    (void)cls;
    Flux *f = (Flux *)(intptr_t)h;
    if (!f) return -1;
    uint8_t *d = (uint8_t *)(*env)->GetDirectBufferAddress(env, dst);
    jlong cap = (*env)->GetDirectBufferCapacity(env, dst);
    if (!d || cap <= 0) return -1;
    int64_t t = 0;
    int n = attendreImage(f, d, (int)(cap > 0x7fffffff ? 0x7fffffff : cap), ms, &t);
    if (infos && (*env)->GetArrayLength(env, infos) >= 4) {
        jlong v[4] = {t, (jlong)f->images, (jlong)f->rejets, (jlong)f->erreur};
        (*env)->SetLongArrayRegion(env, infos, 0, 4, v);
    }
    return n;
}

JNIEXPORT void JNICALL JNI(arreter)(JNIEnv *env, jclass cls, jlong h) {
    (void)env; (void)cls;
    arreterFlux((Flux *)(intptr_t)h);
}

static int plans(JNIEnv *env, Plans *p, jobject y, jint yl, jobject u, jint ul, jint up, jobject v, jint vl, jint vp) {
    p->y = (uint8_t *)(*env)->GetDirectBufferAddress(env, y);
    p->u = (uint8_t *)(*env)->GetDirectBufferAddress(env, u);
    p->v = (uint8_t *)(*env)->GetDirectBufferAddress(env, v);
    if (!p->y || !p->u || !p->v) return -1;
    p->yCap = (long)(*env)->GetDirectBufferCapacity(env, y);
    p->uCap = (long)(*env)->GetDirectBufferCapacity(env, u);
    p->vCap = (long)(*env)->GetDirectBufferCapacity(env, v);
    p->yLigne = yl; p->uLigne = ul; p->uPas = up; p->vLigne = vl; p->vPas = vp;
    return 0;
}

JNIEXPORT jint JNICALL JNI(versPlans)(JNIEnv *env, jclass cls, jint format, jobject src, jint n, jint w, jint h, jint x0, jint y0, jint cw, jint ch,
                                      jobject y, jint yl, jobject u, jint ul, jint up, jobject v, jint vl, jint vp, jint mat) {
    (void)cls;
    const uint8_t *s = (const uint8_t *)(*env)->GetDirectBufferAddress(env, src);
    Plans p;
    if (!s || plans(env, &p, y, yl, u, ul, up, v, vl, vp) < 0) return -1;
    return convertir(format, s, n, w, h, x0, y0, cw, ch, &p, mat);
}

JNIEXPORT jint JNICALL JNI(versNv21)(JNIEnv *env, jclass cls, jint format, jobject src, jint n, jint w, jint h, jbyteArray dst, jint mat) {
    (void)cls;
    const uint8_t *s = (const uint8_t *)(*env)->GetDirectBufferAddress(env, src);
    if (!s) return -1;
    jsize cap = (*env)->GetArrayLength(env, dst);
    if ((long)cap < (long)w * h * 3 / 2) return -1;
    uint8_t *d = (uint8_t *)(*env)->GetPrimitiveArrayCritical(env, dst, NULL);
    if (!d) return -1;
    Plans p;
    p.y = d; p.yLigne = w; p.yCap = (long)w * h;
    p.v = d + (long)w * h; p.vLigne = w; p.vPas = 2; p.vCap = (long)w * h / 2;
    p.u = p.v + 1; p.uLigne = w; p.uPas = 2; p.uCap = (long)w * h / 2 - 1;
    int r = convertir(format, s, n, w, h, 0, 0, w, h, &p, mat);
    (*env)->ReleasePrimitiveArrayCritical(env, dst, d, 0);
    return r;
}

JNIEXPORT jint JNICALL JNI(apercu)(JNIEnv *env, jclass cls, jint format, jobject src, jint n, jint w, jint h, jobject dst, jint pw, jint ph, jint mat) {
    (void)cls;
    const uint8_t *s = (const uint8_t *)(*env)->GetDirectBufferAddress(env, src);
    uint8_t *d = (uint8_t *)(*env)->GetDirectBufferAddress(env, dst);
    if (!s || !d) return -1;
    return apercu(format, s, n, w, h, d, (long)(*env)->GetDirectBufferCapacity(env, dst), pw, ph, mat);
}

#endif
