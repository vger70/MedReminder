# MedReminder — Politique de confidentialité

Dernière mise à jour : 5 octobre 2026

Traduction de [PRIVACY.md](PRIVACY.md). En cas de divergence, la
version anglaise prévaut.

Cette politique décrit comment l'application de bureau MedReminder pour
Windows traite les données personnelles. Elle s'applique à toutes les
distributions de l'application : les paquets ZIP, les programmes
d'installation MSI et le Microsoft Store.

## 1. En bref

- MedReminder conserve vos données sur votre PC. Le développeur
  n'exploite aucun serveur et ne reçoit aucune de vos données.
- Il n'y a ni compte auprès du développeur, ni télémétrie, ni
  statistiques d'utilisation, ni publicité, ni pistage.
- Les données ne quittent votre PC que par des fonctions que vous
  activez : rappels par e-mail, sauvegarde dans le cloud,
  synchronisation et recherche de mises à jour. Chacune n'envoie des
  données qu'au service que vous choisissez.

## 2. Qui est responsable

MedReminder est un logiciel libre et open source (Apache License 2.0)
développé par vger70. Comme le développeur ne collecte ni ne reçoit vos
données, vous en gardez le contrôle : l'application les traite sur
votre propre appareil, pour votre compte.

Contact : info@medreminder26.org, ou un ticket sur
https://github.com/vger70/MedReminder/issues (ne publiez pas de données
de santé dans un ticket public).

## 3. Données conservées sur votre PC

MedReminder conserve tout dans `%LOCALAPPDATA%\MedReminder\`, dans
votre compte Windows :

- profils : nom, rôle, code PIN facultatif (enregistré sous forme de
  hachage) ;
- médicaments, doses, posologies, stocks, prises, ordonnances,
  échéances administratives et notes, une base de données par profil ;
- paramètres e-mail : serveur SMTP, nom d'utilisateur et mot de passe
  (le mot de passe est chiffré avec Windows DPAPI), adresses de
  l'expéditeur et des destinataires, y compris une éventuelle adresse
  du médecin ou de l'aidant ;
- jetons de connexion à OneDrive ou Google Drive, si vous les
  connectez ;
- paramètres, sauvegardes que vous configurez et fichiers journaux.

Ces données concernent votre santé (médicaments et traitement). Les
bases de données ne sont pas chiffrées : toute personne pouvant
utiliser votre compte Windows peut les lire. Le code PIN du profil
évite d'ouvrir le mauvais profil par erreur ; ce n'est pas une
protection. Pour séparer les données d'autres personnes, donnez à
chacune son propre compte Windows.

Les fichiers journaux ne contiennent jamais de mots de passe, de
contenu d'e-mails ni de notes médicales.

La désinstallation de MedReminder ne supprime pas ce dossier.
Supprimez-le pour effacer toutes les données.

## 4. Données qui quittent votre PC

Seules ces fonctions envoient des données, et seulement quand vous les
utilisez :

| Fonction | Ce qui est envoyé | Où |
|---|---|---|
| Rappels par e-mail et demandes d'ordonnance | L'e-mail que vous voyez dans l'application : noms des médicaments, stocks, dates ; une demande d'ordonnance contient aussi le code du produit et votre nom. Ni posologie ni notes | Le serveur SMTP du compte e-mail que vous configurez, puis les destinataires que vous saisissez |
| Sauvegarde dans le cloud (facultative) | Une copie quotidienne de vos profils, chiffrée sur votre PC avec une phrase secrète qui ne quitte jamais le PC | Votre propre OneDrive (dossier de l'application) ou Google Drive (dossier MedReminder), ou un dossier de votre choix |
| Synchronisation entre PC (facultative) | Médicaments, stocks, prises, nom du profil et destinataires, chiffrés de bout en bout | Votre propre OneDrive, Google Drive ou dossier partagé |
| Export vers le calendrier | Un fichier `.ics`, avec des titres génériques sauf si vous choisissez d'inclure les noms des médicaments | Enregistré où vous le choisissez ; les e-mails de stock bas le joignent |
| Recherche de mises à jour et mise à jour du catalogue (activée par défaut, désactivable) | Une demande de la dernière version et des listes publiques de médicaments ; aucune donnée personnelle. GitHub voit votre adresse IP et la version de l'application | GitHub (`api.github.com`, `raw.githubusercontent.com`) |

Quand vous connectez OneDrive ou Google Drive, MedReminder ne demande
l'accès qu'à son propre dossier (OneDrive `Files.ReadWrite.AppFolder` ;
Google Drive `drive.file` et `drive.appdata`), pas à vos autres
fichiers. Désactiver la sauvegarde dans le cloud ou la synchronisation
arrête tout envoi ; l'accès accordé se révoque depuis les paramètres de
votre compte Microsoft ou Google. Ces services traitent les données
selon leurs propres politiques de confidentialité, tout comme votre
fournisseur de messagerie.

## 5. Webcam

Le lecteur de codes-barres peut utiliser votre webcam. Les images sont
décodées sur votre PC et ne sont jamais enregistrées ni envoyées. La
caméra s'éteint quand un code est lu, quand vous fermez la fenêtre de
lecture, ou après 30 secondes.

## 6. Liens qui ouvrent votre navigateur

Certaines commandes ouvrent une page web dans votre navigateur par
défaut : la notice ou la fiche d'information du médicament, le guide
de l'utilisateur, la page du projet et, si vous le choisissez, une page
de don Stripe ou PayPal. MedReminder n'envoie aucune donnée à ces
sites ; c'est votre navigateur qui le fait, selon les politiques de ces
sites. MedReminder ne voit jamais les données de paiement.

## 7. Enfants

MedReminder ne s'adresse pas aux enfants et ne collecte de données
auprès de personne.

## 8. Vos droits

Toutes les données sont sur votre PC, sous votre contrôle : vous pouvez
les consulter, les corriger, les exporter (Paramètres → Sauvegarde /
Restauration → Exporter toutes les données) et les supprimer à tout
moment. Pour les données de votre compte e-mail, OneDrive ou Google
Drive, adressez-vous à ces fournisseurs.

## 9. Modifications

Les modifications de cette politique sont publiées dans ce fichier,
avec une nouvelle date de « Dernière mise à jour ». L'historique est
visible dans le dépôt :
https://github.com/vger70/MedReminder/commits/main/PRIVACY.fr.md
