# Devis & Factures — application mobile pour artisans

Application téléphone (Android / iOS) en **C# / .NET MAUI** permettant à un artisan (peintre, plombier, électricien…)
de gérer ses clients, faire ses **devis** et ses **factures** directement sur le chantier, **sans connexion internet**.
Les données sont stockées dans une base **SQLite locale** sur le téléphone ; le modèle est prêt pour une future synchronisation.

## Fonctionnalités

- **Clients** : fiche (coordonnées, SIRET pour les pros, notes), recherche, historique des devis et factures.
- **Devis** et **factures** :
  - lignes avec quantité, unité (m², h, forfait…), prix HT, TVA par ligne (10 %, 5,5 %, 20 %…), remise ;
  - totaux HT / TVA par taux / TTC calculés en direct, acompte déduit ;
  - brouillon modifiable puis **émission** : numéro définitif continu par année (`D-2026-0001`, `F-2026-0001`),
    document verrouillé ensuite (une facture émise ne se supprime pas, elle s'annule) ;
  - statuts : envoyé / accepté / refusé pour les devis, à payer / payée / annulée pour les factures ;
  - **transformation d'un devis en facture** en un geste, duplication d'un document ;
  - sauvegarde automatique en quittant l'écran.
- **Catalogue de prestations** : « Peinture murs 2 couches — 24 €/m² », réutilisable pour remplir un devis en quelques secondes.
- **Aperçu** du document à montrer au client sur le téléphone, avec zone « Bon pour accord ».
- **Export PDF** (A4, polices intégrées, en-tête de tableau répété sur chaque page) : ouverture dans le lecteur
  PDF du téléphone (impression) ou **envoi** par e-mail, SMS, WhatsApp…
- **Plusieurs pays et devises** : France, Suisse, Belgique, Luxembourg ou « Autre pays » (réglages libres).
  Choisir un pays propose sa devise, ses taux de TVA, ses mentions légales et ses usages ; tout reste modifiable.
- **Mon entreprise** : coordonnées, identifiant (SIRET, IDE, BCE…), assurance, IBAN, non-assujettissement à la TVA
  avec la mention adaptée au pays, délais par défaut, préfixes de numérotation, conditions de paiement.

### Pays pris en charge

| Pays        | Devise | Taux de TVA proposés     | Particularités |
|-------------|--------|--------------------------|----------------|
| France      | EUR    | 20 · 10 · 5,5 · 2,1 %    | SIRET, « TVA non applicable, art. 293 B du CGI », pénalités de retard et indemnité de 40 €, mention « Devis reçu avant l'exécution des travaux » |
| Suisse      | CHF    | 8,1 · 3,8 · 2,6 %        | IDE (CHE-…), montants au format `CHF 1'234.50`, dates `21.10.2026`, **arrondi du total à 0,05** |
| Belgique    | EUR    | 21 · 12 · 6 · 0 %        | N° d'entreprise (BCE), régime de franchise des petites entreprises |
| Luxembourg  | EUR    | 17 · 14 · 8 · 3 %        | N° RCS |
| Autre pays  | au choix | au choix               | libellés génériques |

Devises proposées : EUR, CHF, USD, CAD, GBP, XOF, XAF, MAD, TND, XPF (d'autres codes ISO peuvent être ajoutés
dans `Currencies`). La TVA est **par ligne** : un même document peut mélanger plusieurs taux, détaillés en pied de document.

Chaque document **fige** sa devise, son pays et son régime de TVA à l'émission : changer ces réglages plus tard
ne modifie pas les devis et factures déjà émis. Les brouillons, eux, suivent les réglages courants.

## Architecture

```
src/
  InvoicingTool.Core/          Modèles, base SQLite (sqlite-net), règles métier — net10.0, sans dépendance MAUI
    Models/                    Client, CommercialDocument (devis/facture), DocumentLine, CatalogItem, CompanySettings
    Data/                      AppDatabase + dépôts (suppression logique)
    Models/CountryProfile.cs   Règles par pays (taux, mentions, libellés, format, arrondi) ; Currency.cs : devises
    Services/                  DocumentCalculator (montants), DocumentService (cycle de vie, numérotation),
                               MoneyFormatter (devise + format du pays), PrintableDocument (contenu mis en forme),
                               HtmlDocumentRenderer (aperçu à l'écran)
    Pdf/                       PdfDocumentRenderer (PDFsharp/MigraDoc, licence MIT, 100 % .NET) + polices OpenSans
  InvoicingTool.Presentation/  ViewModels MVVM (CommunityToolkit.Mvvm) — net10.0, testables sans téléphone
  InvoicingTool.App/           Application .NET MAUI : pages XAML, navigation Shell, services natifs (partage, dialogues)
tests/
  InvoicingTool.Tests/         Tests xUnit du cœur métier et des ViewModels
```

Le cœur métier et les ViewModels ne dépendent pas de MAUI : ils sont compilés et testés sur n'importe quelle machine,
et pourront être réutilisés côté serveur pour la synchronisation.

### Préparé pour la synchronisation

Chaque entité possède :

| Champ       | Rôle |
|-------------|------|
| `Id` (GUID) | identifiant global, pas de collision entre appareils |
| `UpdatedAt` | date de dernière modification (UTC), pour détecter les changements |
| `IsDeleted` | suppression logique, pour propager les suppressions |
| `IsDirty`   | modification locale pas encore envoyée (`Repository.GetPendingChangesAsync()`) |

Les documents émis gardent une **copie des coordonnées du client** au moment de l'émission.

> À prévoir avec la synchronisation multi-appareils : la numérotation des factures doit rester continue.
> Pistes : un préfixe par appareil, ou une numérotation attribuée par le serveur.

## Démarrer

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download) et le workload MAUI
(`dotnet workload install maui`), ou Visual Studio 2026 / Rider avec le support MAUI.

```bash
# Tests du cœur métier (toutes plateformes)
dotnet test tests/InvoicingTool.Tests

# Lancer sur un téléphone / émulateur Android branché
dotnet build src/InvoicingTool.App -t:Run -f net10.0-android

# iOS (sur un Mac avec Xcode)
dotnet build src/InvoicingTool.App -t:Run -f net10.0-ios
```

La base de données est créée automatiquement au premier lancement dans le dossier privé de l'application
(`FileSystem.AppDataDirectory/invoicing.db3`).

## Pistes pour la suite

- **Signature du client** au doigt sur le devis, intégrée au PDF.
- **QR-facture suisse** (QR-bill) en bas des factures en CHF.
- Taxes composées (ex. TPS + TVQ au Québec) : le modèle actuel gère un taux par ligne.
- **Synchronisation** avec un serveur / autre système (API REST + champs de synchronisation déjà présents).
- Photos du chantier jointes au devis, relances des factures impayées, factures d'acompte,
  facturation électronique (obligatoire en France à partir de 2026-2027 selon la taille de l'entreprise).
