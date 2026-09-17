# MedReminder — Guide rapide

Guide pratique pour l'utilisateur final. Le fichier
[`ANALYSIS.md`](ANALYSIS.md) décrit à la place l'architecture
technique.

> **MedReminder est un rappel organisationnel, pas un dispositif
> médical.** Il ne fournit ni diagnostic, ni indication
> thérapeutique, ni modification de traitement, ni suggestion
> clinique. Toute décision concernant le traitement doit être prise
> avec ton médecin.

---

## Premier démarrage

1. Lance `MedReminder.exe`.
2. Au premier lancement la fenêtre est vide : la base de données est
   créée automatiquement sous
   `%LOCALAPPDATA%\MedReminder\medreminder.db`.
3. En haut se trouve la barre d'outils ; en bas la barre d'état.
   L'icône dans la zone de notification de Windows reste visible tant
   que l'application est en cours d'exécution.

### SmartScreen de Windows au premier lancement

Les binaires publiés ne sont pas signés numériquement. Au tout
premier lancement de `MedReminder.exe`, Windows affiche une boîte
de dialogue bleue « PC protégé par Windows ». Pour continuer :

1. Clique sur **Informations complémentaires**.
2. Clique sur **Exécuter quand même**.

Windows mémorise ce choix pour ce fichier précis : les lancements
suivants ne redemanderont plus. Si tu installes via le MSI, la
fenêtre UAC indique « Éditeur inconnu » pour la même raison ; c'est
normal.

## Ajouter un médicament

1. Barre d'outils → **Nouveau médicament**.
2. Remplis les champs obligatoires (marqués `*`) : Nom, Unité, Dose
   par prise, Prises par jour, Date de début, Seuil d'alerte (jours
   restants).
3. Champs optionnels : Principe actif, Conditionnement, Date de fin
   de traitement, Médecin de référence, Notes.
4. **Stock initial** : indique les comprimés/ml/doses que tu
   possèdes déjà au moment de l'enregistrement. Un mouvement
   `InitialLoad` est créé.
5. **Canaux de notification** : coche Windows et/ou E-mail. Il faut
   avoir configuré les paramètres SMTP (voir plus bas) pour que
   l'e-mail fonctionne.
6. **Enregistrer**.

## Ajouter du stock (nouvelle boîte)

1. Sélectionne le médicament dans la grille.
2. Barre d'outils → **Ajouter du stock**.
3. Choisis le type de mouvement :
   - **Nouvelle boîte** : cas classique après un achat.
   - **Ajout manuel** : par exemple si tu reçois des échantillons du
     médecin.
   - **Correction positive** : tu avais compté moins que la quantité
     réelle.
4. Saisis la quantité (dans l'unité du médicament) et confirme.

**Effet** : le stock augmente et le `StockEpoch` du médicament est
incrémenté de 1. Cela relance le cycle d'alerte — la prochaine
notification pourra être émise dès que le stock repasse sous le
seuil.

## Corriger une quantité en défaut

Si tu constates que le stock réel est inférieur à celui calculé
(comprimé perdu, renversé, etc.) :

1. Sélectionne le médicament.
2. Barre d'outils → **Corriger le stock**.
3. Le type par défaut est **Correction négative** : la quantité
   saisie est soustraite du stock. Elle ne fait pas avancer
   l'epoch : elle ne reprogramme pas le cycle de notifications.

Si la correction ferait passer le stock sous zéro, l'opération est
bloquée par une erreur.

## Modifier ou désactiver un médicament

- **Modifier** : double-clic sur la ligne, ou barre d'outils →
  **Modifier**. Tu peux changer le nom, le principe actif, le
  conditionnement, l'unité, le seuil, le médecin, les notes, la
  date de fin, les canaux de notification, et l'état Actif/Inactif.
  **La dose et la fréquence NE se modifient PAS d'ici** : utilise
  le changement de posologie (fonction en ligne de commande ou
  édition DB pour le MVP).
- **Désactiver** : barre d'outils → **Désactiver**. Le médicament
  disparaît des contrôles automatiques et des alertes, mais les
  données historiques (mouvements, notifications) restent en base
  pour audit.

## Configurer l'envoi d'e-mails

**Paramètres → E-mail SMTP** :

- **Hôte** : par exemple `smtp.gmail.com`, `smtp-mail.outlook.com`,
  etc.
- **Port** : généralement 587 (StartTLS) ou 465 (SSL/TLS direct).
  MedReminder utilise StartTLS lorsque la case correspondante est
  cochée.
- **Nom d'utilisateur / Nouveau mot de passe** : si le serveur
  demande une authentification. Le mot de passe est chiffré avec
  DPAPI et enregistré dans
  `%LOCALAPPDATA%\MedReminder\smtp.protected`. Il ne figure ni dans
  `smtp.settings.json` ni dans les journaux.
- **Supprimer le mot de passe enregistré** : efface
  `smtp.protected` au prochain enregistrement.
- **Expéditeur / Nom d'expéditeur** : le « from » des e-mails
  envoyés.
- **Destinataire** : où recevoir les alertes (généralement ta
  propre adresse personnelle).
- **Délai** : secondes avant de considérer la connexion échouée.
- **Tester la connexion** : ouvre une session SMTP, s'authentifie,
  ferme. N'envoie pas de véritable e-mail.
- **Enregistrer les paramètres SMTP** : écrit
  `%LOCALAPPDATA%\MedReminder\smtp.settings.json`. La configuration
  est rechargée à chaud, sans redémarrer l'application.

### Exemple : Gmail avec app-password

1. Active la 2FA sur ton compte Google.
2. Crée un app-password sur
   `myaccount.google.com/apppasswords`.
3. Dans MedReminder : Hôte `smtp.gmail.com`, Port `587`,
   StartTLS activé, Nom d'utilisateur `tonadresse@gmail.com`, Mot de
   passe l'app-password que tu viens de créer.

Google et d'autres fournisseurs peuvent modifier leurs exigences :
consulte la documentation de ton fournisseur si le test de connexion
échoue.

## Démarrage automatique avec Windows

**Paramètres → Démarrage automatique** : coche la case. Une entrée
est créée dans
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` qui lance
MedReminder avec l'argument `--minimized` (démarre dans la zone de
notification, fenêtre masquée). Aucun privilège administrateur n'est
requis.

## Sauvegarde de la base de données

**Paramètres → Sauvegarde / Restauration** :

- **Exporter** : choisis un dossier. La base est copiée sous
  `medreminder-YYYYMMDD-HHMMSS.db`. Enregistre la copie sur un
  disque externe ou dans un cloud personnel si tu veux plus de
  résilience.
- **Restaurer** : sélectionne une sauvegarde précédente. La base
  actuelle est renommée en `medreminder.db.bak-<horodatage>` (pas
  perdue !) et remplacée. **Ferme puis rouvre MedReminder** après
  la restauration pour éviter les incohérences.

## Vérifier maintenant

Le moniteur s'exécute automatiquement toutes les 30 minutes
(configurable dans `appsettings.json` à la clé
`Monitoring:IntervalMinutes`). Si tu veux forcer une vérification
immédiate : barre d'outils → **Vérifier maintenant** ou menu de la
zone de notification → **Vérifier maintenant**.

## Icône dans la zone de notification

- **Double-clic** → ouvre la fenêtre.
- **Menu contextuel (clic droit)** :
  - Ouvrir MedReminder
  - Vérifier maintenant
  - Paramètres…
  - Quitter

Fermer la fenêtre principale avec le X la réduit dans la zone de
notification ; l'application continue de tourner en arrière-plan.
Pour quitter réellement : menu de la zone de notification →
**Quitter**.

## Langue de l'interface

**Paramètres → Général** : choisis la langue dans la liste
déroulante (français, anglais, italien ou espagnol) et clique sur
**Enregistrer la langue**. MedReminder redémarre automatiquement
pour appliquer le changement.

Notes :
- Les notifications toast Windows suivent toujours la langue du
  système (Windows), indépendamment de la langue choisie ici.
- Les notifications e-mail et la fiche de traitement utilisent la
  langue sélectionnée ici.

## Diagnostic

- **Journaux** :
  `%LOCALAPPDATA%\MedReminder\logs\medreminder-YYYYMMDD.log`.
  Contient les ticks du planificateur, les envois de notifications,
  les erreurs.
- **Base corrompue ou incompatible** : supprime `medreminder.db`,
  `medreminder.db-shm`, `medreminder.db-wal` sous
  `%LOCALAPPDATA%\MedReminder\`. Au prochain démarrage la base est
  recréée vide. Fais d'abord une sauvegarde manuelle si tu as des
  données importantes.
- **Application déjà en cours** : une seule instance par utilisateur
  Windows. Si le lancement indique « déjà en cours d'exécution »,
  cherche l'icône dans la zone de notification.

## Ce que MedReminder ne fait PAS

- Il ne rappelle pas la prise d'une dose précise (ce n'est pas un
  réveil).
- Il ne fournit pas d'indications thérapeutiques ni d'interactions
  médicamenteuses.
- Il ne synchronise pas entre différents appareils.
- Il ne commande pas de médicaments automatiquement.
- Il ne contacte pas ton médecin directement.

Son unique but est de te prévenir à temps que tu dois demander une
nouvelle ordonnance.
