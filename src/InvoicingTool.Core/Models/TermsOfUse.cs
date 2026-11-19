namespace InvoicingTool.Core.Models;

/// <summary>
/// Conditions générales d'utilisation, à accepter au premier lancement.
/// Augmenter <see cref="Version"/> à chaque modification du texte : l'utilisateur devra les accepter à nouveau.
/// </summary>
/// <remarks>
/// Texte de base à faire relire par un juriste avant publication : en droit suisse, une exclusion de
/// responsabilité ne peut pas couvrir le dol ni la faute grave (art. 100 CO).
/// </remarks>
public static class TermsOfUse
{
    public const string Version = "1.0";

    public const string Title = "Conditions générales d'utilisation";

    public const string Text = """
        1. Objet
        L'application permet de gérer des clients, des articles, des devis et des factures. Elle fonctionne sans connexion internet : toutes les données sont enregistrées uniquement sur cet appareil.

        2. Conservation des données
        L'utilisateur est seul responsable de la conservation de ses données. Elles peuvent être perdues notamment en cas de perte, de vol, de panne ou de réinitialisation de l'appareil, de désinstallation de l'application ou de dysfonctionnement logiciel. L'application propose une fonction d'export (Entreprise > Sauvegarde) : il appartient à l'utilisateur de l'utiliser régulièrement et de conserver ses fichiers de sauvegarde en lieu sûr, en dehors de l'appareil.

        3. Limitation de responsabilité
        Dans toute la mesure permise par la loi, l'éditeur de l'application n'assume aucune responsabilité pour la perte, l'altération ou l'indisponibilité des données, ni pour les dommages directs ou indirects qui pourraient en résulter (notamment perte de chiffre d'affaires, de clientèle ou de documents comptables). Demeure réservée la responsabilité pour dol ou faute grave (art. 100 du Code des obligations).

        4. Obligations légales de l'utilisateur
        L'utilisateur reste responsable du contenu de ses devis et factures, de leur conformité aux règles fiscales et légales qui lui sont applicables (TVA, mentions obligatoires) et de la conservation de ses pièces comptables pendant la durée légale (en Suisse, 10 ans selon l'art. 958f CO). L'application n'est pas un logiciel de comptabilité et ne remplace pas l'avis d'un fiduciaire ou d'un comptable.

        5. Fourniture « en l'état »
        L'application est fournie en l'état, sans garantie d'absence d'erreur ni d'adéquation à un usage particulier.

        6. Droit applicable et for
        Les présentes conditions sont soumises au droit suisse. Le for est au siège de l'éditeur, sous réserve des fors impératifs prévus par la loi.

        En appuyant sur « J'accepte », l'utilisateur déclare avoir lu et accepté ces conditions.
        """;
}
