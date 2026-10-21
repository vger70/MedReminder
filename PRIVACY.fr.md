# MedReminder — Politique de confidentialité

Dernière mise à jour : 11 octobre 2026

Traduction de [PRIVACY.md](PRIVACY.md). En cas de divergence, la version anglaise prévaut.

Cette politique décrit le traitement des données personnelles par MedReminder sous Windows (ordinateur) et Android. Elle s'applique aux versions et fonctions disponibles sur chaque plateforme ; certaines fonctions peuvent être absentes d'une version. L'application Windows est distribuée en paquets ZIP, en installateurs MSI et via le Microsoft Store. Android est distribué via Google Play, y compris ses canaux de test.

## 1. En bref

- MedReminder conserve vos données sur votre appareil. Le développeur n'exploite aucun serveur et ne reçoit pas vos données.
- Il n'y a ni compte auprès du développeur, ni télémétrie, ni statistiques d'utilisation, ni publicité, ni pistage.
- Les données ne quittent l'appareil que par les fonctions que vous utilisez ou activez, comme l'e-mail, l'exportation ou le partage, la sauvegarde cloud, la synchronisation et le téléchargement des catalogues. Chaque fonction transmet les données au seul service ou à la seule destination décrits ci-dessous. Les services propres à une plateforme, comme les mises à jour via Google Play, sont indiqués séparément.

## 2. Responsable

MedReminder est un logiciel libre et open source (Apache License 2.0) développé par vger70. Comme le développeur ne collecte ni ne reçoit vos données, vous en gardez le contrôle : l'application les traite sur votre appareil, pour votre compte.

Contact : info@medreminder26.org, ou un ticket sur https://github.com/vger70/MedReminder/issues (ne publiez pas de données de santé dans un ticket public).

## 3. Données conservées sur votre appareil

### Windows (ordinateur)

MedReminder conserve ses données dans `%LOCALAPPDATA%\MedReminder\`, dans votre compte Windows. Elles comprennent :

- profils : nom, rôle, code PIN facultatif (haché) ;
- médicaments, doses, posologies, stocks, prises, ordonnances, échéances administratives et notes, une base par profil ;
- paramètres e-mail : serveur SMTP, nom d'utilisateur et mot de passe (chiffré avec Windows DPAPI), adresses de l'expéditeur et des destinataires, y compris celle d'un médecin ou aidant si vous en indiquez une ;
- jetons d'accès à OneDrive ou Google Drive, si vous les connectez ;
- paramètres, sauvegardes que vous configurez et journaux.

Sous Windows, les bases des profils ne sont pas chiffrées : toute personne pouvant utiliser votre compte Windows peut les lire. Le code PIN évite d'ouvrir le mauvais profil par erreur ; ce n'est pas une protection. Pour séparer les données, attribuez à chaque personne son propre compte Windows.

Les journaux Windows ne contiennent ni mots de passe, ni contenu d'e-mails, ni notes médicales. La désinstallation de l'application Windows ne supprime pas son dossier de données ; supprimez-le séparément pour effacer ces données.

### Android

MedReminder conserve les bases de données, paramètres et journaux dans l'espace privé de l'application sur le téléphone. La sauvegarde automatique des données de l'application Android est désactivée ; le système ne copie donc pas ces données de santé dans un compte de sauvegarde Android. La phrase secrète de sauvegarde est conservée à l'aide du stockage sécurisé des clés d'Android. L'accès à Google Drive est géré par les services Google Play, qui conservent l'autorisation et fournissent à l'application des jetons d'accès de courte durée. Les jetons de connexion à OneDrive sont conservés par la bibliothèque de connexion Microsoft dans l'espace privé de l'application, que les autres applications ne peuvent pas lire et qui est exclu de la sauvegarde Android. Les journaux Android ne contiennent ni mots de passe, ni contenu d'e-mails, ni notes médicales.

Effacer les données de MedReminder ou désinstaller l'application supprime sa copie locale. Avant cela, utilisez les fonctions d'exportation ou de sauvegarde cloud pour en garder une copie.

## 4. Données qui quittent votre appareil

Ces fonctions transmettent ou partagent des données uniquement lorsqu'elles sont disponibles et utilisées ou activées :

| Fonction | Données transmises | Destination |
|---|---|---|
| Rappels par e-mail et demandes d'ordonnance (si disponibles et configurés) | L'e-mail affiché dans l'application : noms des médicaments, stocks et dates ; une demande d'ordonnance contient aussi le code du produit et votre nom. Ni posologie ni notes | Le serveur SMTP que vous configurez, puis les destinataires saisis |
| Sauvegarde cloud (facultative) | Copie quotidienne de vos profils, chiffrée sur l'appareil avec votre phrase secrète de sauvegarde, qui n'est pas transmise au fournisseur cloud | Votre compte OneDrive ou Google Drive ; sous Windows, également un dossier de votre choix |
| Synchronisation (facultative) | Médicaments, stocks, prises, nom du profil et destinataires, chiffrés de bout en bout | Votre compte OneDrive ou Google Drive ; sous Windows, également un dossier partagé |
| Exportation, calendrier et partage | Archive de profil `.mrz` chiffrée, fichier calendrier `.ics` ou contenu que vous choisissez de partager. Les titres du calendrier sont génériques, sauf si vous choisissez d'inclure les noms des médicaments | Emplacement du fichier ou application/destinataire choisi avec les fonctions de fichiers et de partage de Windows ou Android |
| Mises à jour des catalogues et données publiques | Requêtes de listes publiques de médicaments et autres données publiques ; aucune donnée personnelle de santé | GitHub (`raw.githubusercontent.com`). GitHub reçoit votre adresse IP et les détails de la requête |
| Recherche de mises à jour Windows | Requête pour la dernière version de bureau ; aucune donnée personnelle de santé. GitHub reçoit votre adresse IP et la version de l'application | GitHub (`api.github.com`) |
| Mises à jour Android | Google Play distribue les mises à jour et peut traiter des informations sur le compte, l'appareil ou des diagnostics conformément à sa politique et à vos paramètres | Google Play |

Lorsque vous connectez OneDrive ou Google Drive, MedReminder demande l'accès aux données de l'application et aux fichiers de sauvegarde, et non un accès général à vos autres fichiers. Désactiver une fonction arrête ses futurs envois ; vous pouvez révoquer l'accès dans les paramètres de votre compte Microsoft ou Google. Cela ne supprime pas les copies déjà stockées chez le fournisseur. Les services cloud et de messagerie traitent les données selon leurs propres politiques de confidentialité.

Sous Windows, la sauvegarde cloud et la synchronisation peuvent aussi utiliser les dossiers que vous sélectionnez. Ces dossiers peuvent être synchronisés par un fournisseur cloud ou partagés ; leur accès et leur traitement dépendent du fournisseur et des paramètres du dossier. Sous Android, ces fonctions utilisent le compte OneDrive ou Google Drive connecté. Android ou les paramètres de gestion de la batterie peuvent retarder les opérations planifiées.

## 5. Appareil photo

Le lecteur de codes-barres peut utiliser la webcam Windows ou l'appareil photo Android lorsque vous choisissez de scanner. Les images sont traitées sur l'appareil et ne sont ni enregistrées ni envoyées par MedReminder. Sous Windows, la caméra s'éteint lorsqu'un code est lu, lorsque vous fermez la fenêtre de lecture ou après 30 secondes. Sous Android, son accès dépend de l'autorisation Android et du cycle de vie de l'application. Sous Android, l'appareil photo lit aussi le code QR affiché par un autre appareil lorsque vous rejoignez un cercle ; il n'est utilisé que pendant que la page de lecture est ouverte, et le code n'est pas enregistré.

## 6. Liens qui ouvrent le navigateur

Certaines commandes ouvrent une page web dans votre navigateur, par exemple la notice ou la fiche d'information du médicament, le guide de l'utilisateur ou la page du projet. Sous Windows, un lien de don Stripe ou PayPal peut aussi s'ouvrir si vous le choisissez. MedReminder n'envoie aucune donnée à ces sites ; votre navigateur le fait selon leurs politiques. MedReminder ne voit jamais les données de paiement.

## 7. Enfants

MedReminder ne s'adresse pas aux enfants. Le développeur ne collecte pas de données personnelles d'enfants par l'intermédiaire de l'application.

## 8. Vos droits

Vous pouvez consulter, corriger, exporter et supprimer les données dans MedReminder. Sous Windows, utilisez Paramètres → Sauvegarde / Restauration → Exporter toutes les données ; sous Android, utilisez les fonctions d'exportation et de gestion des données de l'application. Vous pouvez aussi supprimer les données locales de l'application Android dans les paramètres Android. Gérez les copies dans les e-mails, fichiers exportés ou partagés et comptes OneDrive ou Google Drive auprès des destinataires, applications ou fournisseurs concernés.

## 9. Modifications

Les modifications sont publiées dans ce fichier avec une nouvelle date de « Dernière mise à jour ». L'historique est visible dans le dépôt :
https://github.com/vger70/MedReminder/commits/main/PRIVACY.fr.md
