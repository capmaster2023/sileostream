plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
}

// Signature : keystore de production fourni par la CI (secrets), sinon le keystore de debug du dépôt
// (signature stable d'une version à l'autre : les mises à jour s'installent sans perdre le jumelage).
val releaseStore = System.getenv("ANDROID_KEYSTORE_FILE")?.let { file(it) }?.takeIf { it.exists() }

android {
    namespace = "ca.siloestream.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "ca.siloestream.app"
        minSdk = 26
        targetSdk = 35
        // Siloé Stream 1.0 (dépôt capmaster2023/sileostream) : au-dessus des APK 6xxxxx de l'ancien dépôt,
        // pour s'installer par-dessus l'application existante (même package, même signature)
        versionCode = 700000 + (System.getenv("VERSION_CODE") ?: "0").toInt()
        versionName = System.getenv("VERSION_NAME") ?: "1.3.0"
    }

    signingConfigs {
        getByName("debug") {
            storeFile = file("debug.keystore")
            storePassword = "android"
            keyAlias = "androiddebugkey"
            keyPassword = "android"
        }
        if (releaseStore != null) {
            create("release") {
                storeFile = releaseStore
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (releaseStore != null) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
    }
    lint {
        checkReleaseBuilds = false
        abortOnError = false
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.ui)
    implementation(libs.androidx.foundation)
    implementation(libs.androidx.material3)
    implementation(libs.okhttp)
    implementation(libs.camera.camera2)
    implementation(libs.camera.lifecycle)
    implementation(libs.camera.view)
    implementation(libs.mlkit.barcode)
    implementation(libs.credentials)
    implementation(libs.credentials.play)
    implementation(libs.googleid)
}
