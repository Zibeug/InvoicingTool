# Devis & Factures — application simple pour micro-entreprises

Application **téléphone (Android / iOS) et ordinateur Windows** en **C# / .NET MAUI** pour faire ses **devis** et ses **factures** en quelques
minutes, quel que soit le métier : indépendants, consultants, coachs, créatifs, commerçants, services à domicile…
Tout fonctionne **hors ligne** : les données sont stockées dans une base **SQLite locale** sur le téléphone.

## Fonctionnalités

- **Clients** : coordonnées, recherche, historique des devis et factures.
- **Articles** : les produits et services vendus souvent (« Heure de conseil — 120 CHF/h »), pour remplir un document en un geste.
- **Devis et factures** :
  - lignes simples : désignation, quantité, unité, prix ;
  - totaux calculés en direct ;
  - brouillon modifiable puis **émission** avec un numéro définitif continu par année (`D-2026-0001`, `F-2026-0001`) ;
    un document émis n'est plus modifiable (une facture s'annule, elle ne se supprime pas) ;
  - statuts (envoyé, accepté, refusé, à payer, payée, annulée), **devis → facture** en un geste, duplication ;
  - **dictée** : bouton « Dicter des lignes », on parle naturellement
    (« 3 heures de conseil à 120 francs, puis un forfait déplacement à 50 francs ») et les lignes sont créées :
    quantité, unité, désignation, prix unitaire ou total, nombres en chiffres ou en lettres (y compris septante,
    huitante, nonante), articles du catalogue reconnus avec leur prix. Le texte reste modifiable avant l'ajout.
    Reconnaissance vocale du téléphone, hors ligne quand le téléphone le permet ;
  - **signature du devis au doigt** sur l'écran du téléphone : la signature, le nom du signataire, la date et l'heure
    sont imprimés dans le cadre « Bon pour accord » et le devis passe en « accepté » ;
  - **numéro de départ** au choix (ex. continuer à `F-2026-0250` après un ancien logiciel), sans risque de doublon ;
  - sauvegarde automatique en quittant l'écran.
- **PDF** : aperçu à l'écran, ouverture dans le lecteur du téléphone ou envoi par e-mail, SMS, WhatsApp…
- **Personnalisation** : logo de l'entreprise (réduit automatiquement, transparence conservée) et couleur des titres
  et du tableau (10 couleurs proposées ou code `#RRGGBB`).
- **IBAN** imprimé sur les factures avec le titulaire, vérifié (clé de contrôle) et mis en forme par groupes de 4.
- **Pays et devises** : France, Suisse, Belgique, Luxembourg ou autre ; devise au choix (EUR, CHF, USD, CAD, GBP, FCFA…).
- **TVA facultative** : si l'entreprise **n'est pas assujettie**, tout ce qui concerne la TVA disparaît de l'application
  et des documents (pas de taux, pas de « HT » / « TTC », un seul total). Une mention légale peut être imprimée,
  par exemple « TVA non applicable, art. 293 B du CGI » en France (laisser vide si aucune).
- **Sauvegarde des données** (onglet Entreprise) : export de toutes les données dans un fichier JSON à garder hors du
  téléphone (e-mail, Drive, iCloud…), et restauration sur le même ou un nouveau téléphone. Un rappel s'affiche au
  démarrage si aucune sauvegarde n'a été faite depuis 30 jours.
- **Conditions générales d'utilisation** à accepter au premier lancement (droit suisse ; l'application n'est pas
  responsable de la conservation des données), consultables ensuite dans l'onglet Entreprise.

### Pays pris en charge

| Pays        | Devise   | Taux de TVA proposés  | Particularités |
|-------------|----------|-----------------------|----------------|
| France      | EUR      | 20 · 10 · 5,5 · 2,1 % | SIRET, mention art. 293 B du CGI, pénalités de retard |
| Suisse      | CHF      | 8,1 · 3,8 · 2,6 %     | IDE (CHE-…), montants `CHF 1'234.50`, dates `21.10.2026`, arrondi à 0,05 |
| Belgique    | EUR      | 21 · 12 · 6 · 0 %     | n° d'entreprise, régime de franchise des petites entreprises |
| Luxembourg  | EUR      | 17 · 14 · 8 · 3 %     | n° RCS |
| Autre pays  | au choix | au choix              | libellés génériques |

Chaque document **fige** sa devise, son pays et son régime de TVA à l'émission : changer ces réglages plus tard
ne modifie pas les documents déjà émis.

## Architecture

```
src/
  InvoicingTool.Core/          Modèles, base SQLite (sqlite-net), règles métier — net10.0, sans dépendance MAUI
    Models/                    Client, CommercialDocument (devis/facture), DocumentLine, CatalogItem (article),
                               CompanySettings, CountryProfile (règles par pays), Currency, TermsOfUse (CGU)
    Data/                      AppDatabase + dépôts (suppression logique)
    Services/                  DocumentCalculator, DocumentService (cycle de vie, numérotation), MoneyFormatter,
                               PrintableDocument + HtmlDocumentRenderer, BackupService (export / restauration),
                               DictationParser (phrase dictée → lignes)
    Pdf/                       PdfDocumentRenderer (PDFsharp/MigraDoc, licence MIT, 100 % .NET) + polices OpenSans
  InvoicingTool.Presentation/  ViewModels MVVM (CommunityToolkit.Mvvm) — net10.0, testables sans téléphone
  InvoicingTool.App/           Application .NET MAUI : pages XAML, navigation Shell, services natifs,
                               signature (DrawingView) et dictée (OfflineSpeechToText) avec CommunityToolkit.Maui
tests/
  InvoicingTool.Tests/         Tests xUnit du cœur métier et des ViewModels
```

### Fichier de sauvegarde

JSON lisible (`format: "InvoicingTool.Backup"`, `version: 1`) contenant les paramètres, clients, articles, documents,
lignes, signatures, logo et compteurs de numérotation. La restauration vérifie le fichier avant de toucher aux données, puis remplace
tout en une seule transaction (en cas d'erreur, rien n'est modifié). La numérotation continue donc sans doublon.

### Préparé pour la synchronisation

Chaque entité possède un `Id` (GUID), `UpdatedAt`, `IsDeleted` (suppression logique) et `IsDirty`
(modification pas encore envoyée, voir `Repository.GetPendingChangesAsync()`).

## Conditions d'utilisation

Le texte est dans `src/InvoicingTool.Core/Models/TermsOfUse.cs`. Il est **à faire relire par un juriste** avant
publication : en droit suisse, une exclusion de responsabilité ne peut pas couvrir le dol ni la faute grave
(art. 100 CO), et le for doit être précisé. Modifier le texte → augmenter `TermsOfUse.Version` pour que les
utilisateurs l'acceptent à nouveau.

## Démarrer

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download) et le workload MAUI (`dotnet workload install maui`),
ou Visual Studio / Rider avec le support MAUI.

```bash
dotnet test tests/InvoicingTool.Tests                              # tests (toutes plateformes)
dotnet build src/InvoicingTool.App -t:Run -f net10.0-android       # téléphone / émulateur Android
dotnet build src/InvoicingTool.App -t:Run -f net10.0-ios           # iOS (sur un Mac avec Xcode)
dotnet build src/InvoicingTool.App -t:Run -f net10.0-windows10.0.19041.0  # Windows (sur un PC Windows)
```

## Windows

L'application fonctionne aussi sur un PC **Windows 10 (version 1809) ou Windows 11**, en application classique
(`InvoicingTool.App.exe`, sans Microsoft Store ni installation).

- **Version prête à l'emploi** : dans l'onglet *Actions* du dépôt GitHub, ouvrir la dernière exécution réussie du
  workflow *Tests*, télécharger l'artefact `DevisFactures-windows-x64`, le décompresser et lancer `InvoicingTool.App.exe`.
  Tout le nécessaire est inclus (.NET et Windows App SDK).
- **Depuis les sources** : Visual Studio 2026 avec la charge de travail *.NET MAUI*, choisir la cible *Windows Machine* ;
  ou en ligne de commande :

  ```bash
  dotnet build src/InvoicingTool.App -t:Run -f net10.0-windows10.0.19041.0
  ```

Différences avec le téléphone :
- « Envoyer le PDF » et « Exporter » la sauvegarde ouvrent une boîte **Enregistrer sous** (au lieu du partage) ;
- la signature se fait à la souris, au stylet ou au doigt sur un écran tactile ;
- la dictée utilise la reconnaissance vocale de Windows : installer la voix française dans
  *Paramètres > Heure et langue > Voix* si nécessaire ;
- les données sont stockées dans le dossier de l'utilisateur (`%LOCALAPPDATA%`). Elles sont **propres à chaque appareil** :
  pour passer du téléphone au PC, exporter une sauvegarde sur l'un et la restaurer sur l'autre.

## Pistes pour la suite

- QR-facture suisse en bas des factures en CHF.
- Sauvegarde automatique vers un dossier cloud choisi par l'utilisateur, puis synchronisation entre appareils.
