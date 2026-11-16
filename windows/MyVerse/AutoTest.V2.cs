namespace MyVerse.Ui;

public static partial class AutoTest
{
    /// <summary>
    /// Siloé Stream 2.0.0 : les écrans simples du design « Grand Bouton », chacun avec sa capture (90 à 96) :
    /// Bonjour, la Régie (Commencer le culte, le gros Suivant), la barre magique (Montrer), Préparer le culte, le son, le direct,
    /// puis le retour à la régie complète.
    /// </summary>
    static async Task TestVersion2(MainWindow w)
    {
        var R = App.Regie;
        // un culte de test : un chant, un passage, un texte
        R.NouvelAgenda("Culte du dimanche (test 2.0)");
        var chant = R.Chants.FirstOrDefault(c => c.Title.Contains("toi la gloire")) ?? R.Chants.FirstOrDefault();
        if (chant != null) R.AjouterChantAgenda(chant.Id);
        R.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
        R.AjouterTexteAgenda("Bénédiction", "Que Dieu vous bénisse\n\nÀ dimanche prochain");
        R.Effacer();
        await Attendre(300);

        w.TestV2Aller("accueil");
        await Attendre(900);
        Ok(w.TestV2Visible && w.TestV2Page == "accueil", "2.0.0 : écran Bonjour");
        Capture(w, "90-v2-bonjour.png");

        w.TestV2Commencer();
        await Attendre(900);
        Ok(w.TestV2Page == "regie" && R.ElementCourant != null, "2.0.0 : Commencer le culte : la régie montre la première carte (" + (R.ElementCourant?.Titre ?? "rien") + ")");
        Ok(w.TestV2Cartes == R.Agenda.Elements.Count, $"2.0.0 : le culte en cartes ({w.TestV2Cartes} sur {R.Agenda.Elements.Count})");
        Capture(w, "91-v2-regie.png");
        var avant = R.Etat.Ref + "|" + string.Join("/", R.Etat.Lignes);
        Note("2.0.0 : avant Suivant : " + avant + " · " + w.TestV2Puis);
        w.TestV2Suivant();
        await Attendre(700);
        var apres = R.Etat.Ref + "|" + string.Join("/", R.Etat.Lignes);
        Ok(apres != avant, "2.0.0 : le gros Suivant avance : " + apres);
        Capture(w, "92-v2-regie-suivant.png");

        w.TestV2Barre("jean 3 16");
        await Attendre(900);
        Ok(w.TestV2BarreOuverte && w.TestV2Resultats > 0, $"2.0.0 : barre magique : « jean 3 16 » ({w.TestV2Resultats} résultat(s))");
        Capture(w, "93-v2-barre-magique.png");
        w.TestV2BarreValider(false);
        await Attendre(700);
        Ok(!w.TestV2BarreOuverte && R.Etat.Ref.StartsWith("Jean 3:16"), "2.0.0 : Montrer (Entrée) : " + R.Etat.Ref);

        w.TestV2Aller("preparer");
        await Attendre(700);
        Ok(w.TestV2Page == "preparer", "2.0.0 : Préparer le culte");
        Capture(w, "94-v2-preparer.png");
        w.TestV2Aller("son");
        await Attendre(900);
        Ok(w.TestV2Page == "son", "2.0.0 : le son, en gros boutons");
        Capture(w, "95-v2-son.png");
        w.TestV2Aller("direct");
        await Attendre(900);
        Ok(w.TestV2Page == "direct", "2.0.0 : Aller en direct");
        Capture(w, "96-v2-direct.png");
        w.TestV2Aller("classique");
        await Attendre(300);
        Ok(!w.TestV2Visible, "2.0.0 : Mode complet : la régie de toujours revient");
    }
}
