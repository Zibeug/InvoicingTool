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
- **Aperçu** du document à montrer au client sur le téléphone, avec zone « Bon pour accord », et **partage**
  (e-mail, SMS, WhatsApp…) sous forme de page HTML imprimable.
- **Mon entreprise** : coordonnées, SIRET, assurance décennale, IBAN, franchise de TVA (art. 293 B du CGI),
  délais par défaut, préfixes de numérotation, mentions de pénalités de retard.

## Architecture

```
src/
  InvoicingTool.Core/          Modèles, base SQLite (sqlite-net), règles métier — net10.0, sans dépendance MAUI
    Models/                    Client, CommercialDocument (devis/facture), DocumentLine, CatalogItem, CompanySettings
    Data/                      AppDatabase + dépôts (suppression logique)
    Services/                  DocumentCalculator (montants), DocumentService (cycle de vie, numérotation),
                               HtmlDocumentRenderer (rendu du document)
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

- Génération **PDF** native (en plus de l'HTML partagé) et **signature du client** au doigt sur le devis.
- **Synchronisation** avec un serveur / autre système (API REST + champs de synchronisation déjà présents).
- Photos du chantier jointes au devis, relances des factures impayées, factures d'acompte,
  facturation électronique (obligatoire en France à partir de 2026-2027 selon la taille de l'entreprise).
