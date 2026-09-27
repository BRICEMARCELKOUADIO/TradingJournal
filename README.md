# TradingJournal

Socle backend ASP.NET Core / EF Core SQL Server, ciblant .NET 10.

## Organisation

- Domain : aucune dépendance de projet ; entités User, Trade, Strategy, Emotion et TradeImage,
  enums métier et catalogue des sept émotions prédéfinies.
- Application → Domain : DTO, validation, AuthService, TradeService et interfaces
  d'accès aux données.
- Infrastructure → Application, Domain : AddInfrastructure, DbContext, configurations
  IEntityTypeConfiguration et migrations SQL Server, repository utilisateur,
  hachage PBKDF2 et émission des JWT.
- Api → Application, Infrastructure : contrôleurs d'authentification et de trades, validation JWT,
  accès centralisé à l'utilisateur courant, limitation des requêtes et OpenAPI.
- Tests : xUnit, démarrage OpenAPI, domaine, mappings EF Core et intégration SQL Server.

Les cinq entités métier et les deux tables techniques de gestion des images sont enregistrées dans TradingJournalDbContext avec
ApplyConfigurationsFromAssembly. Les migrations sont appliquées explicitement ;
le démarrage de l'API ne crée ni ne modifie automatiquement la base.
Le frontend React/TypeScript sera ajouté dans une étape ultérieure.

## Commandes

Depuis la racine, avec le SDK .NET 10 :

```powershell
dotnet restore TradingJournal.sln
dotnet tool restore
dotnet build TradingJournal.sln --configuration Release --no-restore
dotnet test TradingJournal.sln --configuration Release --no-build
dotnet run --project src/TradingJournal.Api --launch-profile https
```

Configurer le secret JWT décrit ci-dessous avant de lancer l'API.
Document OpenAPI en développement : https://localhost:7150/openapi/v1.json.
Il décrit les endpoints d'authentification, de trades et d'images. Interface Scalar :
https://localhost:7150/scalar/v1.

La configuration de développement actuelle utilise SQL Server sur localhost, base TradingJournal. Adapter ConnectionStrings:TradingJournal pour une autre instance.
En dehors du développement, fournir la variable d'environnement
ConnectionStrings__TradingJournal ou une configuration équivalente ; aucun mot de passe
ne doit être commité. Le démarrage et les tests hors intégration ne nécessitent pas
de serveur SQL actif. Les tests SQL sont activés explicitement ci-dessous.

EF Core Design est une dépendance privée de l'API, utilisée par les outils EF.

## Authentification : configuration locale

L'API refuse de démarrer sans configuration JWT valide. Aucun secret de signature
n'est fourni dans les fichiers du dépôt. Le projet API possède un UserSecretsId ;
pour créer une clé locale aléatoire de 256 bits, depuis PowerShell :

```powershell
$authKeyBytes = New-Object byte[] 32
$authRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$authRng.GetBytes($authKeyBytes)
$authRng.Dispose()
@{ 'Jwt:SigningKey' = [Convert]::ToBase64String($authKeyBytes) } | ConvertTo-Json -Compress | dotnet user-secrets set --project src/TradingJournal.Api
dotnet run --project src/TradingJournal.Api --launch-profile https
```

Cette commande remplace une éventuelle clé locale existante et invalide alors les
anciens jetons. User Secrets est destiné au développement, pas au stockage de
secrets en production. Autre possibilité : fournir `Jwt__SigningKey` par variable
d'environnement, depuis un gestionnaire de secrets, sans inscrire sa valeur dans
un fichier versionné. Les tests génèrent leurs propres clés temporaires en mémoire.

| Paramètre | Valeur / règle |
| --- | --- |
| `Jwt:SigningKey` / `Jwt__SigningKey` | Obligatoire : Base64 de 32 à 64 octets aléatoires |
| `Jwt:Issuer` / `Jwt__Issuer` | `TradingJournal` par défaut, non vide |
| `Jwt:Audience` / `Jwt__Audience` | `TradingJournal.Client` par défaut, non vide |
| `Jwt:LifetimeMinutes` / `Jwt__LifetimeMinutes` | 15 par défaut, de 1 à 60 |
| `ConnectionStrings:TradingJournal` | Connexion existante ; variable `ConnectionStrings__TradingJournal` selon environnement |
| `AuthRateLimiting:LoginPermitLimit` | 5 requêtes par IP et fenêtre, de 1 à 100 |
| `AuthRateLimiting:RegisterPermitLimit` | 5 requêtes par IP et fenêtre, compteur distinct, de 1 à 100 |
| `AuthRateLimiting:WindowSeconds` | 60 secondes par défaut, de 1 à 3600 |

Les paramètres de limitation acceptent aussi les variables d'environnement avec
`__` à la place de `:`.

## Endpoints d'authentification

Utiliser HTTPS et `Content-Type: application/json` pour les requêtes POST.
Les mots de passe ci-dessous sont uniquement des exemples.

```http
POST /api/auth/register
Content-Type: application/json

{"displayName":"Alice","email":"Alice@example.com","password":"Exemple-long-2026!"}
```

Réponse 201 : `{"id":"<guid>","displayName":"Alice","email":"alice@example.com"}`.
Nom requis (100 caractères maximum), email valide (255 maximum), mot de passe de
12 à 128 caractères, sans troncature ni normalisation du mot de passe. Le nom et
l'email sont nettoyés des espaces périphériques ; l'email est mis en minuscules
invariantes. 400 pour les données invalides, 409 pour un email déjà utilisé, y
compris lors d'inscriptions concurrentes. Aucun PasswordHash n'est retourné.

```http
POST /api/auth/login
Content-Type: application/json

{"email":"alice@example.com","password":"Exemple-long-2026!"}
```

Réponse 200 : `{"accessToken":"<JWT>","expiresAtUtc":"<date UTC>","user":{"id":"<guid>","displayName":"Alice","email":"alice@example.com"}}`.
LastLoginAt est mis à jour après une authentification réussie. Une adresse inconnue
et un mauvais mot de passe produisent le même message 401 : « Email ou mot de passe
incorrect. ». Les données structurellement invalides produisent une réponse 400.

```http
GET /api/auth/me
Authorization: Bearer <JWT obtenu à la connexion>
```

Réponse 200 : uniquement `id`, `displayName` et `email` de l'utilisateur identifié
par le claim `sub`. Réponse 401 si le JWT est absent, expiré, mal signé, mal formé,
destiné à un autre émetteur/destinataire, ou si l'utilisateur n'existe plus.
`ICurrentUser`, implémenté par `HttpCurrentUser`, centralise l'identifiant authentifié.
Les réponses d'authentification ne doivent pas être mises en cache (`no-store`).

## Choix de sécurité et limites à valider avant déploiement

- Les mots de passe utilisent le PBKDF2-HMAC-SHA256 de .NET, 600 000 itérations,
  un sel aléatoire de 128 bits et un résultat de 256 bits. Le format est versionné.
  La comparaison utilise FixedTimeEquals ; un utilisateur absent ou un hash invalide
  déclenche également le calcul PBKDF2. Aucun ASP.NET Identity ni stockage en clair.
  Référence : [OWASP Password Storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).
- JWT HS256 avec `sub`, `jti`, `iat`, `nbf` et `exp`. Validation de la signature,
  de l'algorithme, du type, de l'émetteur, du destinataire et de la durée de vie,
  sans tolérance d'horloge. Synchroniser les horloges et conserver la clé hors du dépôt.
  Référence : [validation JWT ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
- Pas de refresh token ni de révocation individuelle dans cette mission : un JWT
  reste valable jusqu'à son expiration ou au remplacement de la clé. La politique
  de rotation et de stockage côté client reste à définir avant la mise en production.
- Limitation par IP, en mémoire et par instance, sur login et register, y compris
  les requêtes réussies ou invalides. Au-delà : 429 avec Retry-After, sans file d'attente.
  Un redémarrage remet les compteurs à zéro. Pour plusieurs instances ou pour
  contrer des essais distribués entre plusieurs IP, prévoir une protection partagée.
  X-Forwarded-For n'est pas accepté aveuglément ; configurer les proxies de confiance
  si l'API est placée derrière un reverse proxy. Les clients derrière une même IP
  partagent actuellement le quota.
- Le statut 409 d'inscription permet de déduire qu'un email est déjà utilisé :
  ce compromis est explicite, contrairement aux erreurs de connexion génériques.
  EmailVerified reste false ; la connexion est autorisée sans vérification par email.
  Aucun système d'email ou de réinitialisation n'est ajouté.
- L'index unique existant utilise `SQL_Latin1_General_CP1_CI_AS`, insensible à la
  casse, vérifié sur la base actuelle. Aucune migration n'est nécessaire. Si une
  autre base est utilisée, préserver une collation insensible à la casse et lancer
  les tests SQL ; ne pas supposer que les collations de toutes les instances sont identiques.

## API des trades (mission 5)

Tous les endpoints exigent `Authorization: Bearer <accessToken>` et retournent
401 sans JWT valide. L'identifiant du propriétaire vient exclusivement du JWT.
Les DTO d'écriture refusent les champs inconnus, notamment `userId`. Un paramètre
de requête `userId` ne peut pas modifier le périmètre des lectures.
Les détails, modifications, clôtures et suppressions d'un trade inexistant ou
appartenant à autrui retournent le même 404. Les corps de requête invalides sont
validés indépendamment de l'existence du trade et retournent 400.

| Méthode | Route | Résultat |
| --- | --- | --- |
| POST | `/api/trades` | 201 avec DTO et Location ; création ouverte ou clôturée |
| GET | `/api/trades` | 200 avec `items`, `page`, `pageSize`, `totalCount` |
| GET | `/api/trades/{id}` | 200 avec les champs du trade, sans entité EF ni données utilisateur |
| PUT | `/api/trades/{id}` | 200 avec DTO après remplacement complet |
| POST | `/api/trades/{id}/close` | 200 après clôture ; 409 si déjà clôturé |
| DELETE | `/api/trades/{id}` | 204 après suppression ; 404 s'il n'existe plus |

Enums JSON numériques : MarketType = Forex **0**, SyntheticIndex **1**, Stock **2** ;
Direction = Buy **0**, Sell **1** ; Status = Open **0**, Closed **1**.
Le statut de création vaut Open s'il est omis. Les autres champs obligatoires
sont marketType, symbol, direction, positionSize, entryDateTime et entryPrice.

Exemple de création ouverte (entêtes HTTPS et Bearer comme pour `/api/auth/me`) :

```http
POST /api/trades
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "marketType": 0,
  "symbol": "EURUSD",
  "direction": 0,
  "positionSize": 0.10,
  "entryDateTime": "2026-09-27T10:00:00Z",
  "entryPrice": 1.12345678,
  "stopLoss": 1.12000000,
  "takeProfit": 1.13000000,
  "status": 0,
  "strategyId": null,
  "entryEmotionId": "10000000-0000-0000-0000-000000000001",
  "entryReason": "Signal conforme au plan",
  "notes": "Observation à l'entrée"
}
```

Pour créer un trade déjà clôturé, envoyer le même corps avec `status: 1` et
ajouter les trois propriétés obligatoires :

```json
{
  "exitDateTime": "2026-09-27T11:00:00Z",
  "exitPrice": 1.12500000,
  "resultAmount": 12.50
}
```

Pour clôturer un trade existant :

```http
POST /api/trades/<id>/close
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "exitDateTime": "2026-09-27T11:00:00Z",
  "exitPrice": 1.12500000,
  "resultAmount": 12.50,
  "exitEmotionId": "10000000-0000-0000-0000-000000000002",
  "exitReason": "Objectif atteint",
  "planRespected": true
}
```

`resultAmount` est le montant monétaire saisi : gain, perte ou zéro, jamais un
pourcentage ni un calcul automatique à partir des prix. `positionSize` désigne
les lots pour Forex/SyntheticIndex et le nombre d'actions pour Stock ; les quantités
fractionnaires positives sont autorisées.

Exemples de consultation et suppression :

```http
GET /api/trades?page=1&pageSize=20&startDate=2026-09-01T00:00:00Z&endDate=2026-09-30T23:59:59Z&marketType=Forex&symbol=EURUSD&status=Closed&direction=Buy
Authorization: Bearer <accessToken>

GET /api/trades/<id>
Authorization: Bearer <accessToken>

DELETE /api/trades/<id>
Authorization: Bearer <accessToken>
```

Tous les filtres sont facultatifs et combinés par ET. Les bornes startDate/endDate
portent sur la date d'entrée et sont inclusives, à l'instant exact fourni. Les
horodatages d'écriture acceptent un décalage UTC et sont stockés en UTC ; les
réponses portent le suffixe Z. Fournir de préférence des dates ISO 8601 avec Z ou
un décalage explicite. Le symbole est nettoyé et mis en majuscules ; son filtre
effectue une égalité, pas une recherche partielle.

Le tri est décroissant par EntryDateTime puis Id pour départager les dates égales.
Page commence à 1, pageSize vaut 20 par défaut et doit rester entre 1 et 100.
Les valeurs invalides ou une pagination dépassant la capacité SQL sont refusées
avec 400. Une page au-delà de l'historique retourne une liste vide.
Le comptage et la page utilisent deux requêtes SQL filtrées par propriétaire,
avec AsNoTracking ; aucune navigation n'est chargée et il n'y a pas de requête N+1.
Une écriture concurrente peut changer l'historique entre deux pages : cette pagination
ne constitue pas un instantané immuable.

### Modification et décisions de validation

Pour modifier, utiliser `PUT /api/trades/<id>` avec le corps complet du POST,
par exemple en remplaçant `notes` par une nouvelle annotation. Les propriétés
optionnelles omises deviennent nulles. Le PUT ne modifie ni Id, ni UserId, ni
CreatedAt ; UpdatedAt est actualisé. Les relations d'images sont conservées.

- Un PUT peut clôturer un trade ouvert en utilisant `Trade.Close(...)`.
- Un trade clôturé peut recevoir des corrections aux champs d'entrée et annotations,
  mais doit conserver son statut et ses trois données de sortie : aucune réouverture
  ou réécriture de ces données (409). La date d'entrée corrigée doit rester antérieure
  ou égale à la sortie. Cette restriction préserve la règle de clôture unique du domaine.
- Un trade ouvert ne peut pas recevoir exitDateTime, exitPrice ou resultAmount sans
  passer à Closed : 400, plutôt qu'une perte silencieuse des données reçues.
- L'appel `/close` conserve les annotations optionnelles existantes si elles sont
  omises ou nulles. Pour effacer ces annotations, utiliser ensuite un PUT complet.
- Quantité et prix, y compris les stops/objectifs renseignés, sont strictement positifs.
  Les dates de sortie ne précèdent pas l'entrée. Les longueurs suivent les colonnes SQL.
  Une précision dépassant (18,6), (18,8) ou (18,2) selon le champ est refusée avec 400,
  sans arrondi implicite par cette API.
- Une stratégie doit être globale ou appartenir au propriétaire du trade ; une stratégie
  étrangère ou inexistante donne le même 400. Les émotions référencées doivent exister.
- PUT et clôture s'exécutent dans une transaction avec verrou SQL UPDLOCK/HOLDLOCK
  sur le trade appartenant à l'utilisateur. Deux clôtures concurrentes produisent
  un succès et un 409. Les UPDATE et DELETE incluent aussi le prédicat UserId.
  Aucun rowversion ni changement de schéma n'est nécessaire. Deux PUT successifs
  restent des remplacements complets : le dernier gagne, sans contrôle ETag.

### Captures d'écran privées (mission 6)

Les cinq endpoints JWT sous `/api/trades/{tradeId}/images` permettent de demander
un PUT présigné, confirmer le fichier, lister les métadonnées, obtenir un GET
présigné et supprimer une image. Les DTO historiques des trades restent inchangés.

Voir le [guide S3 complet](docs/S3-images.md) pour les exemples Scalar, les entêtes
PUT obligatoires, les identifiants AWS locaux, les politiques IAM/CORS/lifecycle,
les limites, les réponses d'erreur et le nettoyage après panne.
S3 est désactivé par défaut ; configurer bucket, région et chaîne AWS puis activer
`TradeImages:S3:Enabled`. Aucun secret AWS n'est versionné.

## Migration et connexion SQL Server

Migration initiale : `20260926192041_InitialCreate`, dans
`src/TradingJournal.Infrastructure/Persistence/Migrations`, avec son designer,
le snapshot du modèle et le script SQL idempotent `InitialCreate.sql`.
Depuis la racine, après compilation Release :

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet ef database update --project src/TradingJournal.Infrastructure --startup-project src/TradingJournal.Api --configuration Release --no-build
```

La connexion de développement existante cible `localhost`, base
`TradingJournal`. Pour une autre instance, fournir
la vraie chaîne via `ConnectionStrings__TradingJournal` avant la commande ; aucun
paramètre de connexion alternatif n'est supposé. `dotnet-tools.json` fixe l'outil
local `dotnet-ef` en 10.0.12, comme les packages EF Core. Exécuter `dotnet tool restore`
depuis la racine après récupération du dépôt. La factory de conception dans l'API
charge la configuration SQL sans démarrer l'authentification ni exiger de secret JWT.
`dotnet ef migrations has-pending-model-changes` (avec les mêmes paramètres de projet
et de configuration que ci-dessus) confirme que le modèle n'a pas changé.

Les émotions sont insérées par `HasData(PredefinedEmotions.Create())` dans la
migration. Leurs identifiants restent inchangés. L'historique EF empêche une seconde
application de la migration et donc une seconde insertion ; il n'y a pas de seed
automatique à chaque démarrage de l'API.

## Suppressions et contraintes

- User → Trades et User → Strategies : SQL `NO ACTION`.
- Strategy → Trades et les deux relations Emotion → Trades : SQL `NO ACTION`.
  Une entité encore référencée ne peut pas être supprimée. Pour autoriser sa suppression,
  il faudrait détacher explicitement ses références optionnelles au préalable.
- Trade → TradeImages : SQL `CASCADE`, y compris si les images ne sont pas chargées
  par EF. Seules les lignes de métadonnées sont supprimées ; la suppression physique
  des fichiers désignés par StorageKey sera la responsabilité du futur service de stockage.

Les relations sans cascade utilisent `ClientNoAction` pour empêcher aussi EF de
mettre implicitement à NULL les références des entités suivies. Il n'existe qu'un
chemin de cascade SQL Server. La contrainte `CK_Trades_ClosedRequiresExit` accepte
les statuts Open/Closed et interdit une clôture sans date, prix ou résultat, même
pour une écriture SQL directe. Elle ne change pas la méthode métier `Trade.Close`.

FileName et ContentType restent obligatoires, conformément aux propriétés non
nullables du domaine. L'unicité d'Email suit la collation SQL Server de la base ;
la couche Application normalise désormais les emails à l'inscription et à la connexion.

## Tests SQL Server

Pour exécuter la suite complète sur l'instance SQL Server configurée :

```powershell
$env:TRADINGJOURNAL_SQLSERVER_TEST_CONNECTION = (Get-Content src/TradingJournal.Api/appsettings.Development.json -Raw | ConvertFrom-Json).ConnectionStrings.TradingJournal
dotnet test TradingJournal.sln -c Release --no-build --no-restore
```

Pour une autre instance de test accessible, définir cette variable avec sa vraie
chaîne de connexion. Les tests remplacent le nom de base par
`TradingJournal_Tests_<guid>`, créent cette base isolée, appliquent deux fois les
migrations puis la suppriment. Ils n'utilisent pas la base de développement pour
leurs écritures. Le compte doit pouvoir créer et supprimer la base temporaire.
Sans cette variable, les vingt-sept tests SQL sont explicitement ignorés ; une connexion
configurée mais inaccessible fait échouer les tests, sans faux succès.

## Validation historique de la mission 5

Validation le 27 septembre 2026 après déplacement hors de OneDrive, avec le SDK
.NET 10.0.401 : restauration réussie, compilation Release avec 0 erreur et
0 avertissement, et 110 tests xUnit réussis (0 échec, 0 ignoré) après la mission 5.
Les 85 tests précédents sont préservés ; les 25 nouveaux couvrent la validation des
trades, les six endpoints protégés, l'isolation entre propriétaires, les références,
les clôtures concurrentes, les images, les filtres et la pagination SQL.
Au total, 19 tests s'exécutent sur SQL Server réel.
EF 10.0.12 confirme que le modèle et la base sont à jour ; aucune migration ajoutée.
L'audit NuGet, incluant les dépendances transitives, ne signale aucun package
vulnérable dans les sources consultées lors de cette validation.

SQL Server 2019 LocalDB 15.0.4382.1 est disponible. La migration initiale a été
appliquée à la base de développement TradingJournal. Vérification SQL effectuée :
cinq tables métier (plus `__EFMigrationsHistory`), sept émotions, six clés étrangères,
index attendus et précisions decimal conformes. Aucune base temporaire de test ne
subsiste après la validation. Les accès LocalDB ont nécessité une exécution sous
le compte Windows propriétaire de l'instance, hors du bac à sable de l'agent.

Les commandes standard ci-dessus fonctionnent depuis
`C:\Users\kbric\Projects\Project\TradingJournal`, sans `--artifacts-path`.
Les chemins évalués par MSBuild pour les cinq projets utilisent leurs dossiers
locaux `bin\Release\net10.0\` et `obj\Release\net10.0\` ; les fichiers de
restauration NuGet sont dans leurs dossiers `obj\`. Aucun chemin de sortie
ne pointe vers OneDrive. Le contournement par un dossier temporaire n'est plus
nécessaire.

## Règles métier du domaine

Un trade est ouvert à sa création et ses données de sortie sont nulles.
`Trade.Close(exitDateTime, exitPrice, resultAmount)` exige les trois informations
avant toute modification, passe le statut à Closed et renseigne UpdatedAt en UTC.
Une seconde clôture est refusée. Les setters privés des données de sortie et du
statut empêchent une clôture incomplète via l'API publique du domaine et restent
utilisables par EF Core. Le résultat est fourni explicitement : aucun calcul
automatique commun aux différents marchés n'est supposé.

Une stratégie sans UserId est globale. `PredefinedEmotions.Create()` fournit les
sept émotions avec des identifiants stables et des instances indépendantes ; leur
insertion en base est gérée par la migration. Les deux relations Trade–Emotion sont distinguées par les
attributs ForeignKey de la bibliothèque standard, sans dépendance EF Core dans Domain.
Les configurations Fluent de l'Infrastructure précisent toutes les relations.
Les propriétés du domaine utilisent decimal ; les colonnes utilisent (18,6) pour
PositionSize, (18,8) pour les prix et (18,2) pour ResultAmount. SQL Server peut
arrondir les valeurs dépassant ces échelles ; le domaine conserve sa précision
decimal sans ajouter de règle d'arrondi métier.

## Fichiers de la mission 4

Fichiers créés :

- `dotnet-tools.json` : outil EF local en 10.0.12.
- `src/TradingJournal.Application/Auth/` : `RegisterRequest.cs`, `LoginRequest.cs`,
  `EmailNormalizer.cs`, `AuthResponses.cs`, `IUserRepository.cs`, `IPasswordHasher.cs`,
  `IAccessTokenService.cs`, `ICurrentUser.cs`, `AuthService.cs`.
- `src/TradingJournal.Infrastructure/Auth/` : `UserRepository.cs`,
  `Pbkdf2PasswordHasher.cs`, `JwtOptions.cs`, `JwtTokenService.cs`.
- `src/TradingJournal.Api/Auth/` : `AuthenticationConfiguration.cs`,
  `AuthRateLimitOptions.cs`, `HttpCurrentUser.cs`.
- `src/TradingJournal.Api/Controllers/AuthController.cs` et
  `src/TradingJournal.Api/Persistence/TradingJournalDbContextFactory.cs`.
- `tests/TradingJournal.Tests/` : `AuthApiFactory.cs`, `AuthUnitTests.cs`,
  `AuthEndpointTests.cs`, `SqlServerAuthTests.cs`.

Fichiers modifiés : les deux `DependencyInjection.cs` d'Application et Infrastructure,
les `.csproj` d'Infrastructure et Api, `Api/Program.cs`, `Api/appsettings.json`,
`tests/TradingJournal.Tests/StartupTests.cs`, `tests/TradingJournal.Tests/SqlServerFixture.cs`
et ce README. La mission 4 n'avait modifié ni les entités Domain, ni les mappings EF,
ni la migration.

## Fichiers de la mission 5

Créés :

- `src/TradingJournal.Application/Trades/` : `TradeWriteRequest.cs`, `CloseTradeRequest.cs`,
  `TradeQuery.cs`, `TradeResponse.cs`, `SqlDecimalAttribute.cs`, `ITradeRepository.cs`,
  `TradeConflictException.cs`, `TradeService.cs`.
- `src/TradingJournal.Infrastructure/Persistence/TradeRepository.cs`.
- `src/TradingJournal.Api/Controllers/TradesController.cs` et `TradeExceptionFilterAttribute.cs`.
- `tests/TradingJournal.Tests/TradeValidationTests.cs`, `TradeAuthorizationTests.cs`,
  `SqlServerTradeTests.cs`.

Modifiés : `Domain/Entities/Trade.cs` (ajout de MarkUpdated uniquement, Close inchangé),
les `DependencyInjection.cs` d'Application et Infrastructure, et ce README.
Aucun projet, package, migration, frontend, service S3 ou statistique ajouté.

Références : [OpenAPI ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0)
et [fournisseur SQL Server EF Core](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/).


## Mission 6 : fichiers et validation

Créés :

- `Application/Images/` : contrats et validation, abstraction `ITradeImageStorage`,
  repository, service et vérification des signatures de fichiers.
- `Infrastructure/Images/` : adaptateur AWS S3, options, repository SQL,
  réservations persistantes et file de suppression avec reprises.
- `Infrastructure/Persistence/Configurations/ImageWorkflowConfiguration.cs`.
- Migration `20260927041103_AddImageUploadWorkflow`, designer, snapshot et script
  idempotent `AddImageUploadWorkflow.sql` : ajout de `ImageUploads` et
  `ImageDeletions`, sans changement des cinq tables métier.
- `Api/Controllers/TradeImagesController.cs`, `Api/Images/ImageCleanupWorker.cs`.
- Tests `FakeTradeImageStorage.cs`, `TestImageBytes.cs`, `ImageValidationTests.cs`,
  `SqlServerImageTests.cs`.
- `docs/S3-images.md` et quatre exemples JSON dans `docs/s3/`.

Modifiés : les injections de dépendances Application/Infrastructure, le package
AWS SDK S3 dans Infrastructure, `TradeRepository.cs` (suppression S3 différée),
le filtre d'erreurs API, `Program.cs`, `appsettings.json`, la factory de tests,
les assertions du modèle/migrations dans les tests existants et ce README.
Les chemins de projets ci-dessus sont relatifs à `src/TradingJournal.*` ; les tests
se trouvent dans `tests/TradingJournal.Tests`. Aucun projet n'a été recréé.

La suppression SQL et la demande de suppression S3 sont atomiques ; le worker
réessaie ensuite S3. La confirmation est idempotente et les quotas sont sérialisés
par trade dans SQL Server. Le serveur vérifie les octets reçus puis les enregistre
sous une clé distincte inaccessible aux PUT présignés du client. Les URL GET
utilisent une courte durée et une disposition attachment. La validation de signature
et de structure ne constitue pas un antivirus ni un décodage intégral de pixels.

La migration a été appliquée à la base de développement existante sur localhost.
S3 réel reste à valider sur le compte cible : bucket privé, IAM minimal, CORS,
chiffrement, lifecycle, reprise du nettoyage et absence de journalisation des URL.

Validation finale du 27 septembre 2026 : `dotnet build TradingJournal.sln -c Release
--no-restore` réussit avec **0 avertissement et 0 erreur**. `dotnet test` en Release
réussit : **132 tests, 0 échec, 0 ignoré**, dont 27 tests SQL Server réel ; S3 est
simulé. Les 110 tests précédents sont préservés et 22 tests ont été ajoutés.
Rapport local : `TestResults/mission6.trx`.
