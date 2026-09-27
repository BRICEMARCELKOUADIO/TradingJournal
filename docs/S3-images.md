# Captures de trades — mission 6

## Configuration

Le code utilise AWSSDK.S3 et sa chaîne standard d'identification. Aucun identifiant
AWS n'est enregistré dans SQL, le code ou les fichiers de configuration du dépôt.
Le stockage est désactivé par défaut ; les endpoints qui nécessitent S3 répondent
503 tant qu'il n'est pas configuré. L'authentification et les trades restent utilisables.

Renseigner des valeurs réelles via User Secrets ou variables d'environnement :

```powershell
$env:TradeImages__S3__Enabled = 'true'
$env:TradeImages__S3__BucketName = '<nom réel du bucket privé>'
$env:TradeImages__S3__Region = '<région réelle du bucket>'
$env:AWS_PROFILE = '<profil local existant>'
dotnet run --project src/TradingJournal.Api --launch-profile https
```

Pour créer un profil local, utiliser la commande interactive `aws configure --profile
tradingjournal-dev` puis `AWS_PROFILE=tradingjournal-dev`. L'AWS CLI écrit les
identifiants dans le dossier personnel `.aws`, hors du dépôt. Ne pas les recopier
dans appsettings, le terminal de l'agent ou le README. En production sur EC2/ECS,
utiliser le rôle de l'instance/de la tâche ; ne pas définir de clés statiques ni de
profil local. Les profils SSO ou rôles fédérés nécessitent également les providers
SDK correspondants : cette livraison est validée pour la résolution standard des
profils classiques et des rôles EC2/ECS, sans test d'un compte AWS réel.

| Paramètre | Défaut / limites |
| --- | --- |
| `TradeImages:MaxImagesPerTrade` | 10 ; configurable de 1 à 100, demandes actives comprises |
| `TradeImages:PutUrlSeconds` | 300 ; de 30 à 600 secondes |
| `TradeImages:GetUrlSeconds` | 120 ; de 30 à 300 secondes |
| `TradeImages:UploadLifetimeMinutes` | 15 ; de 15 à 60 minutes |
| `TradeImages:S3:CleanupEnabled` | true ; worker actif seulement si S3.Enabled=true |
| Taille maximale | 10 Mio = 10 485 760 octets, fixe ; minimum 1 octet |

Les variables d'environnement remplacent `:` par `__`. La clé JWT et la connexion
SQL existantes restent nécessaires, sans modification de leurs conventions.

## Bucket et IAM

Créer/configurer le bucket hors de l'application, dans la région choisie :

- Activer les quatre réglages **S3 Block Public Access**, au niveau du compte et
  du bucket. Ne configurer aucun accès public ni hébergement de site web.
- Activer Object Ownership **Bucket owner enforced** : ACL désactivées.
- Activer le chiffrement par défaut SSE-S3 (AES256). Les écritures finales du
  serveur demandent également AES256. SSE-KMS demanderait une configuration et
  des permissions KMS supplémentaires, non incluses ici.
- Exiger TLS avec la bucket policy fournie. Le SDK ne génère que des URL HTTPS.
- Utiliser de préférence un bucket sans versioning pour ce flux. Avec versioning,
  DeleteObject pose un marqueur ; ajouter une politique de rétention/expiration
  des versions non courantes et des marqueurs, sinon les octets restent stockés.
- Appliquer obligatoirement le lifecycle des objets temporaires : il couvre aussi
  la réutilisation d'un PUT après un premier nettoyage et les uploads commencés
  avant expiration mais terminés tardivement.

Modèles à adapter, sans les appliquer automatiquement :

- [iam-policy.json](s3/iam-policy.json) : GetObject, PutObject et DeleteObject sur
  les deux préfixes uniquement, sans gestion de bucket ni permission d'ACL.
- [bucket-policy.json](s3/bucket-policy.json) : refus des accès sans TLS.
- [cors.json](s3/cors.json) : origine locale Scalar autorisée explicitement ; adapter
  aux origines réelles, sans wildcard d'origine en production.
- [lifecycle.json](s3/lifecycle.json) : expiration de `trade-images/staging/` après
  un jour ; ne jamais appliquer cette expiration aux images confirmées.

Remplacer `BUCKET_NAME` dans les policies par le bucket réel. Le rôle applicatif
n'a pas besoin de ListBucket, PutObjectAcl ou d'autorisations d'administration.
Sans ListBucket, S3 peut répondre 403 plutôt que 404 à une lecture d'objet absent :
l'API échoue alors de façon sûre avec 503, au lieu du 409 habituel pour NoSuchKey.
Ne pas accorder de droits larges uniquement pour distinguer ces deux cas.

## Endpoints et utilisation dans Scalar

Ouvrir `/scalar/v1` en Development, obtenir un JWT via `/api/auth/login`, puis
ajouter l'entête `Authorization: Bearer <accessToken>` aux requêtes API. Si le
document Scalar ne présente pas de schéma Bearer, utiliser l'éditeur d'entêtes.
Les nouvelles routes sont présentes dans le document OpenAPI existant.

Toutes les routes sont préfixées par `/api/trades/{tradeId}/images` :

| Méthode | Suffixe | Réponse |
| --- | --- | --- |
| POST | `/uploads` | 200 : uploadId, uploadUrl, expiresAtUtc, requiredHeaders |
| POST | `/uploads/{uploadId}/confirm` | 200 : métadonnées de l'image validée |
| GET | aucun | 200 : liste triée par DisplayOrder, CreatedAt puis Id |
| GET | `/{imageId}/download-url` | 200 : url, expiresAtUtc |
| DELETE | `/{imageId}` | 204 : référence supprimée, nettoyage S3 durablement planifié |

Les données invalides donnent 400, les quotas/expirations ou objets non téléversés
409, un stockage indisponible 503. Sans JWT valide : 401. Pour les trades, images
ou demandes appartenant à autrui ou inconnus : 404. Aucun UserId client n'est
accepté ; les champs JSON inconnus de la demande sont refusés.

### 1. Demander une URL PUT

Dans Scalar, utiliser un trade existant appartenant au compte connecté :

```http
POST /api/trades/<tradeId>/images/uploads
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "imageType": 0,
  "fileName": "entree.png",
  "contentType": "image/png",
  "sizeBytes": 123456,
  "displayOrder": 0
}
```

ImageType : Before=0, After=1. sizeBytes doit être le nombre exact d'octets du
fichier réel, pas une estimation. Extensions autorisées : .jpg/.jpeg, .png, .webp,
associées respectivement à image/jpeg, image/png, image/webp. Les chemins, caractères
de contrôle et noms de plus de 255 caractères sont refusés. DisplayOrder : 0 à 10000.

La réponse contient une URL PUT temporaire et les entêtes requis Content-Type,
Content-Length et x-amz-meta-upload-id. Elle ne comporte pas de champ StorageKey.
Une URL S3 contient nécessairement la clé de l'objet dans son chemin : la traiter
comme une capacité temporaire confidentielle, sans la journaliser ni la partager.

### 2. Envoyer le fichier directement à S3

Cette requête ne passe pas par l'API. Envoyer le **corps binaire brut**, sans
multipart/form-data et sans entête Authorization JWT, à uploadUrl, avec les valeurs
exactes de requiredHeaders. Avec curl, par exemple :

```sh
curl --request PUT "$UPLOAD_URL" \
  --header 'Content-Type: image/png' \
  --header "x-amz-meta-upload-id: $UPLOAD_METADATA_ID" \
  --data-binary @entree.png
```

curl et les navigateurs calculent Content-Length depuis le fichier/Blob ; sa valeur
doit correspondre à celle signée. Ne pas utiliser `curl -v`, des logs HTTP détaillés
ou un proxy de débogage enregistrant l'URL. Ne pas confondre uploadId, qui est un
GUID dans les routes API, et la valeur exacte d'entête retournée par requiredHeaders.

### 3. Confirmer et consulter

```http
POST /api/trades/<tradeId>/images/uploads/<uploadId>/confirm
Authorization: Bearer <accessToken>
```

Aucun corps n'est nécessaire. L'API lit réellement l'objet S3, vérifie sa taille,
son MIME, sa métadonnée signée et son contenu avant d'insérer TradeImage.
La réponse contient uniquement id, imageType, fileName, contentType, displayOrder,
createdAt. id est identique à uploadId. Une répétition de la confirmation retourne
la même image, sans nouveau fichier final ni doublon SQL.

```http
GET /api/trades/<tradeId>/images
Authorization: Bearer <accessToken>

GET /api/trades/<tradeId>/images/<imageId>/download-url
Authorization: Bearer <accessToken>

DELETE /api/trades/<tradeId>/images/<imageId>
Authorization: Bearer <accessToken>
```

Les GET présignés expirent rapidement, utilisent `Content-Disposition: attachment`
et `Cache-Control: no-store`. Les réponses API contenant des URL portent également
no-store. Une image supprimée ne peut plus obtenir de nouvelle URL ni être confirmée
à nouveau. Une URL déjà remise peut rester utilisable jusqu'à son expiration ou à
la suppression effective de l'objet : une URL présignée n'est pas révocable individuellement.

## Contrôle du contenu et immutabilité

La longueur, le MIME et l'identifiant de téléversement sont signés dans le PUT.
L'API relit le contenu avec une limite mémoire de 10 Mio, refuse les encodages de
transport tels que gzip et compare les métadonnées aux valeurs réservées en SQL.
Elle contrôle les signatures et structures PNG/JPEG/WebP : CRC des chunks PNG,
marqueurs JPEG, taille/conteneur RIFF WebP et dimensions plafonnées à 40 millions
de pixels. Les fichiers tronqués et données ajoutées après la fin sont refusés.
Le WebP animé est volontairement exclu pour ce flux de captures.

Ces contrôles ne sont ni un décodage complet des pixels ni un antivirus. Le téléchargement
en pièce jointe évite de servir des contenus actifs inline. Avant d'autoriser un
affichage inline ou des transformations, prévoir un décodage/réencodage isolé et,
selon le contexte, un scanner antivirus. Les limites de pixels et le refus du WebP
animé sont des choix produit à valider.

Le PUT ne vise qu'une clé aléatoire `trade-images/staging/`. Après validation,
l'API écrit **les octets exacts qu'elle a lus**, sous une autre clé aléatoire
`trade-images/confirmed/`, puis enregistre TradeImage. Elle ne copie pas un objet
mutable par sa seule clé. Réutiliser le PUT ne peut donc pas altérer une image
confirmée. Les confirmations et quotas sont sérialisés par verrou SQL sur le trade.

## Transactions, suppression et nettoyage

Deux tables techniques sont ajoutées sans toucher aux cinq tables métier :

- ImageUploads : reservation durable, valeurs annoncées, clés temporaires/finales,
  expiration et état confirmé. Elle n'est pas supprimée en cascade avec le trade :
  le nettoyage doit encore pouvoir trouver les objets après cette suppression.
- ImageDeletions : file durable des clés à supprimer, nombre d'essais et prochaine
  date de tentative. Elle ne contient ni secret ni URL présignée.

Une écriture S3 suivie d'un échec SQL laisse la reservation en attente : ses deux
clés sont nettoyées à expiration. Si SQL avait effectivement validé la transaction,
une répétition de confirmation retrouve la même image. Après expiration, une
reservation confirmée ne fait supprimer que l'objet temporaire.

La suppression d'une image retire sa ligne SQL et ajoute la clé à ImageDeletions
**dans la même transaction**. La suppression d'un trade fait de même pour toutes
ses images avant la cascade SQL. Le worker essaie S3 ensuite : une panne ne perd
pas la demande. Un succès S3 suivi d'une panne SQL est réessayé sans danger puisque
DeleteObject est idempotent. Les endpoints historiques de trades gardent leurs
statuts et formats de réponse, y compris DELETE 204.

Le worker passe toutes les 30 secondes, traite au maximum 100 reservations expirées
puis 100 suppressions par passage, et réessaie les pannes avec un délai croissant
de 30 secondes à 32 minutes. Il ne journalise ni clés, ni URL, ni exceptions SDK
brutes. Surveiller opérationnellement le volume, Attempts et NextAttemptAtUtc dans
ImageDeletions ; aucune suppression en échec n'est abandonnée automatiquement.

Le lifecycle S3 d'un jour sur staging est un filet de sécurité obligatoire, notamment
si le worker est arrêté. Les objets confirmés ne doivent pas expirer aveuglément.
Pour les interventions SQL/S3 hors API ou une restauration partielle de sauvegarde,
prévoir une réconciliation régulière d'un inventaire S3 avec TradeImages et les
reservations : traiter seulement les objets sans référence, plus anciens qu'un
délai de grâce d'au moins 24 heures, après revue. Ce traitement administratif utilise
un rôle séparé avec accès à l'inventaire ; le rôle applicatif n'a pas ListBucket.

## Migration et validation locale

Migration : `20260927041103_AddImageUploadWorkflow`, avec script SQL idempotent dans
`src/TradingJournal.Infrastructure/Persistence/Migrations/AddImageUploadWorkflow.sql`.
Aucune migration n'est appliquée au démarrage de l'API. Pour un environnement
explicitement choisi, après sauvegarde/revue du script :

```powershell
dotnet tool restore
dotnet build TradingJournal.sln -c Release
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet ef database update --project src/TradingJournal.Infrastructure --startup-project src/TradingJournal.Api --configuration Release --no-build
```

Le 27 septembre 2026, la migration a été appliquée explicitement à la configuration
locale existante **localhost / TradingJournal**. Aucune base de production n'a été
ciblée, et aucune configuration de connexion existante n'a été remplacée.
Les tests utilisent des bases SQL temporaires et ITradeImageStorage simulé. Les
tests de signature AWS ne font aucun appel réseau et utilisent des identifiants
factices générés en mémoire. Aucun bucket réel, IAM, CORS ou lifecycle AWS n'a été
créé ni testé par cette mission : leur configuration reste à vérifier sur le compte cible.

## Références AWS

- [Téléversements présignés](https://docs.aws.amazon.com/AmazonS3/latest/userguide/PresignedUrlUploadObject.html) : une URL PUT peut être réutilisée et remplacer un objet, d'où la séparation staging/confirmed.
- [Chaîne d'identification .NET](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html) : profils locaux et rôles IAM.
- [SDK GetPreSignedUrlRequest](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TGetPreSignedUrlRequest.html) : métadonnées, entêtes et expiration des signatures.
