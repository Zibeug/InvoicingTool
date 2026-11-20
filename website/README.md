# Site de présentation (bêta gratuite)

Site statique : trois pages HTML, une feuille de style, deux polices hébergées localement, aucun cookie,
aucun traceur, aucun script.

| Fichier | Contenu |
|---|---|
| `index.html` | Accueil : présentation, fonctionnalités, bêta gratuite, données, questions |
| `mentions-legales.html` | Mentions légales (éditeur, hébergeur) |
| `confidentialite.html` | Déclaration de protection des données (LPD) |
| `assets/` | Feuille de style, logo, favicon, polices (SIL Open Font License) |

## À compléter avant la mise en ligne

1. **Adresse e-mail** : remplacer `beta@votre-domaine.ch` dans les trois pages.
2. **Mentions légales** et **protection des données** : remplacer les passages surlignés en orange
   (`[…]`) par l'identité de l'éditeur, l'adresse, l'hébergeur du site et celui du serveur de synchronisation.
3. Plateformes : la page indique Windows « disponible sur demande », Android et iPhone « bientôt ».
   Mettre à jour la section « Bêta » quand les versions mobiles seront publiées.

## Déployer

Le site n'a besoin que d'un hébergement de fichiers statiques. Au choix :

- **Avec l'API, sur le même serveur** : `docker compose --profile website up -d` (nginx, port 8081, réglable
  avec `WEBSITE_PORT`). Pour le HTTPS, placer un proxy (Caddy, Traefik) devant, comme pour l'API.
- **GitHub Pages** : activer Pages (Settings > Pages > Source : GitHub Actions), puis lancer le workflow
  « Site web » depuis l'onglet Actions. (Sur un dépôt privé, Pages demande un abonnement GitHub payant.)
- **Hébergeur suisse** (Infomaniak, Hostpoint…) ou Netlify / Cloudflare Pages : déposer le contenu du dossier
  `website/` tel quel.

Aperçu local : ouvrir `index.html` dans un navigateur, ou `python3 -m http.server -d website 8081`.
