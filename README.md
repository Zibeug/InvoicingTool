# Devis & Factures — application simple pour micro-entreprises

Application **téléphone (Android / iOS) et ordinateur Windows** en **C# / .NET MAUI** pour faire ses **devis** et ses **factures** en quelques
minutes, quel que soit le métier : indépendants, consultants, coachs, créatifs, commerçants, services à domicile…
Tout fonctionne **hors ligne** : les données sont stockées dans une base **SQLite locale** sur le téléphone.

## Fonctionnalités

- **Tableau de bord** (onglet Accueil) : chiffre d'affaires de l'année et du mois, graphique des 12 derniers mois,
  encaissé / à encaisser, devis en attente, **factures à relancer** et, pour une entreprise non assujettie,
  **alerte au seuil de TVA** (CHF 100 000 en Suisse, réglable).
- **Rappels de paiement** : 1er rappel, 2e rappel, dernier rappel, chacun avec un nouveau délai et des frais
  réglables (cumulés). Chaque rappel est un PDF avec sa QR-facture (montant frais compris, même référence RF
  que la facture). Les factures échues apparaissent « En retard (12 j) » dans les listes.
- **Paiements** : bouton « Marquer comme payée » dans la facture (avec la date réelle du paiement), depuis le
  tableau de bord pour les factures en retard, et « Annuler le paiement » en cas d'erreur.
- **Mode clair / sombre** (onglet Entreprise) : réglage propre à l'application, indépendant de Windows et
  d'Android. Appliqué immédiatement, enregistré avec les données (donc inclus dans la sauvegarde).
- **Export pour la fiduciaire** (onglet Entreprise) : un ZIP par année avec la liste des factures (CSV pour Excel),
  la ventilation de la TVA, un journal d'écritures à importer dans **Banana Comptabilité** (comptes PME
  1100 / 3400 / 2200 / 1020, à adapter) et le PDF de chaque facture.
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
- **QR-facture suisse** sur les factures en CHF : récépissé et section paiement avec code QR, conformes aux normes
  SIX 2025 (adresses structurées), en bas de la dernière page ou sur une page à part si elle est pleine.
  Référence de paiement « RF… » tirée du numéro de facture (IBAN normal ; les QR-IBAN ne sont pas pris en charge).
  L'onglet Entreprise indique en direct si tout est prêt ou ce qu'il manque.
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
                               DictationParser (phrase dictée → lignes), DashboardService (chiffres clés),
                               ReminderService (rappels), AccountingExportService (export fiduciaire)
    Pdf/                       PdfDocumentRenderer (PDFsharp/MigraDoc, licence MIT, 100 % .NET) + polices OpenSans
                               et Liberation Sans ; QrBill/ : dessin de la QR-facture (SwissQRBill, licence MIT)
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

## QR-facture suisse

Générée avec [SwissQRBill.NET](https://github.com/manuelbl/SwissQRBill.NET) (`Codecrete.SwissQRBill.Core`, MIT),
dessinée dans le PDF avec l'intégration PDFsharp de ses exemples (`src/InvoicingTool.Core/Pdf/QrBill`).
La section paiement utilise Liberation Sans (police admise par les normes, licence SIL OFL,
voir `src/InvoicingTool.Core/Fonts/LICENSE-LiberationSans.txt`).

Conditions : entreprise en Suisse, IBAN suisse ou liechtensteinois normal, facture en CHF, émise ou en brouillon
(pas pour une facture payée ou annulée). Le client (débiteur) est imprimé si son code postal et sa ville sont connus ;
sinon le cadre « Payable par » reste vide, comme le prévoit la norme.

> Avant la mise en production, scanner une facture avec une application bancaire ou la contrôler avec le portail de
> validation de SIX. Les tests vérifient le contenu du code QR et la mise en page, pas l'acceptation par une banque.

## Synchronisation entre appareils (concept, facultative)

L'utilisateur peut, s'il le souhaite, retrouver ses données sur plusieurs appareils (téléphone, PC…).
Désactivée par défaut : onglet **Entreprise > Paramétrer la synchronisation**.

```
src/
  InvoicingTool.Sync.Contracts/  Formats d'échange (JSON) partagés par l'application et l'API
  InvoicingTool.Api/             API ASP.NET Core + PostgreSQL (EF Core), image Docker
  InvoicingTool.Core/Sync/       Client de l'API et moteur de synchronisation de l'application
tests/
  InvoicingTool.Api.Tests/       Tests de l'API (vraie base PostgreSQL) et de bout en bout (deux « appareils »)
docker-compose.yml               API + PostgreSQL (+ HTTPS automatique en option)
```

**Fonctionnement**
- Compte par e-mail et mot de passe (haché avec l'algorithme d'ASP.NET Core Identity). Jeton d'accès JWT de
  60 minutes, jeton de renouvellement à usage unique (60 jours) : réutiliser un ancien jeton ferme les sessions
  de l'appareil. Tentatives de connexion limitées par adresse IP. Jetons stockés dans le trousseau (iOS),
  le Keystore (Android) ou chiffrés par Windows (DPAPI).
- Chaque appareil garde sa base locale et fonctionne hors ligne. Une synchronisation **envoie** les éléments
  modifiés localement puis **reçoit** ceux des autres appareils. Le serveur numérote les modifications
  (version croissante par compte) ; un appareil demande « ce qui a changé depuis la version N ».
- Le serveur stocke chaque élément tel quel (JSON) : il ne dépend pas du modèle de données de l'application
  (et pourra plus tard ne recevoir que des données chiffrées de bout en bout).
- **Conflits** : si le même élément est modifié sur deux appareils, la dernière modification reçue par le serveur
  l'emporte ; une modification locale pas encore envoyée n'est jamais écrasée. Les documents émis ne se modifient
  plus, les conflits ne concernent donc que les brouillons, clients, articles et paramètres.
- **Numérotation** : le premier appareil garde sa série (`F-2026-0012`), chaque appareil ajouté reçoit sa lettre
  (`F-2026-B-0001`) : aucun numéro ne peut être attribué deux fois, même hors ligne.
- Un appareil qui rejoint le compte reçoit d'abord les données du compte (ses paramètres vides n'écrasent pas
  ceux de l'entreprise). L'acceptation des CGU et la date de dernière sauvegarde restent propres à chaque appareil.
- Synchronisation automatique à l'ouverture et au retour dans l'application, et bouton « Synchroniser maintenant ».
- `DELETE /api/account` supprime le compte et toutes ses données (droit à l'effacement).

**Lancer le serveur**

```bash
cp .env.example .env          # renseigner POSTGRES_PASSWORD et JWT_SECRET (openssl rand -base64 48)
docker compose up -d --build  # API sur http://<serveur>:8080 (santé : /api/health)

# Mise en ligne avec HTTPS automatique (Let's Encrypt) : DOMAIN=sync.mon-entreprise.ch dans .env, puis
docker compose --profile https up -d --build
```

La base PostgreSQL n'est pas exposée hors de Docker ; les tables sont créées au démarrage de l'API (migrations EF Core).
Pour tester avec un téléphone, le serveur HTTP local est accepté en mode Debug (Android) et sur le réseau local (iOS) ;
en production, utiliser HTTPS.

**Points d'accès** : `POST /api/auth/register`, `/login`, `/refresh`, `/logout`, `DELETE /api/account`,
`POST /api/sync/push`, `GET /api/sync/pull?since=N`, `GET /api/health`.

**Tests** : `dotnet test tests/InvoicingTool.Api.Tests` (PostgreSQL requis, chaîne de connexion dans
`INVOICING_TEST_POSTGRES`, par défaut `Host=localhost;Username=postgres;Password=postgres`).

**Limites du concept (à traiter avant une mise en production)**
- Les conditions d'utilisation doivent mentionner l'hébergement des données sur le serveur (nLPD).
- Restaurer une sauvegarde sur un appareil synchronisé renvoie tout l'historique ; à utiliser avec prudence.
- Un appareil qui avait déjà émis des factures avant de rejoindre un compte garde ses anciens numéros.
- Pas encore de gestion des appareils (liste, retrait), de réinitialisation du mot de passe ni d'abonnement.

## Pistes pour la suite

- Synchronisation : chiffrement de bout en bout, gestion des appareils, réinitialisation du mot de passe, abonnement.
