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
2. Au tout premier lancement l'application affiche un **assistant
   de bienvenue** et te demande de créer le premier profil. Ce
   profil est toujours l'**administrateur** : il peut gérer le
   serveur e-mail partagé et la sauvegarde automatique, et créer
   les autres profils (voir *Profils multiples*). Tu peux définir
   un PIN facultatif dans le même assistant.
3. La base de données est créée automatiquement sous
   `%LOCALAPPDATA%\MedReminder\profiles\<id-profil>\medreminder.db`.
4. En haut se trouve la barre d'outils ; en bas la barre d'état
   affiche le profil actif ("Profil : Owner (administrateur)" pour
   un administrateur, "Profil : Grand-mère" pour un utilisateur
   normal). L'icône dans la zone de notification de Windows reste
   visible tant que l'application est en cours d'exécution.

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
6. **Schéma** : laisse sur **Simple** pour une dose fixe prise
   chaque jour — c'est la valeur par défaut, identique au
   fonctionnement historique de l'application. Voir *Schémas
   complexes* ci-dessous pour les traitements cycliques,
   décroissants, hebdomadaires ou au besoin.
7. **Enregistrer**.

## Schémas complexes

Tous les traitements ne consomment pas la même quantité de
médicament chaque jour. Sur le formulaire **Nouveau médicament**, le
sélecteur *Schéma* bascule de **Simple** (dose journalière fixe) à
**Avancé** et fait apparaître un menu *Type de régime* avec quatre
formes supplémentaires :

- **Motif hebdomadaire** — une quantité différente pour chaque jour
  de la semaine (par exemple un anticoagulant oral pris à des doses
  différentes les lun/mer/ven par rapport aux autres jours).
- **Cyclique (N jours on / M off)** — une quantité fixe pendant les
  `N` premiers jours du cycle puis `M` jours off. Typique des
  traitements hormonaux et des bolus de cortisone.
- **Décroissance** — une dose qui diminue (ou augmente)
  progressivement jusqu'à la dose finale. Le panneau Décroissance
  propose deux variantes via le sélecteur *Linéaire / Par paliers* :
  - **Linéaire** — la dose varie d'un pas fixe tous les X jours
    jusqu'à la dose finale, puis se stabilise. Typique d'une
    décroissance simple de glucocorticoïdes.
  - **Par paliers** — une liste explicite de paliers, chacun avec
    sa propre dose et sa propre durée en jours (par exemple 4/jour
    pendant 7 jours, puis 2/jour pendant 7 jours, puis 1/jour
    pendant 14 jours). Utilise *Ajouter un palier* / *Supprimer*
    pour construire la séquence et consulte l'aperçu en temps réel
    sous la liste avant d'enregistrer. Coche *Maintenir la dernière
    dose comme dose d'entretien* si la dose finale doit se
    poursuivre indéfiniment plutôt que de terminer le traitement.
- **Au besoin (PRN)** — aucune consommation planifiée. MedReminder
  continue à suivre le stock mais la colonne *jours restants* reste
  vide tant que le type de schéma ne change pas.

Quand tu passes en Avancé, les champs *Dose par prise*, *Prises par
jour* et *Horaires* en haut du formulaire sont désactivés : le
schéma que tu configures en bas est la seule source pour la
quantité journalière. Pour revenir au flux "un clic" avec dose
fixe, remets le sélecteur sur Simple.

Pour changer la forme d'un traitement en cours, utilise *Barre
d'outils → Changer la posologie*. Le même sélecteur Simple/Avancé y
est disponible et s'applique à partir de la *Date d'effet* que tu
choisis, de sorte que le schéma précédent reste valable pour les
jours antérieurs à cette date.

Si la *Date d'effet* est dans le passé, la consommation automatique
déjà enregistrée à partir de cette date est recalculée avec le
nouveau schéma lors du contrôle suivant. Les mouvements de stock
enregistrés avant l'installation de cette version ne sont jamais
recalculés.

MedReminder n'est pas un dispositif médical : il ne vérifie pas les
doses maximales journalières, n'alerte pas sur les surdosages et ne
contrôle pas les interactions médicamenteuses. Il suit uniquement la
prescription de ton médecin et te prévient avant que le stock ne
s'épuise.

## Rappel à l'heure de la prise

Pour les médicaments qui ont des **créneaux de prise avec horaire** (un
moment précis de la journée défini sur chaque créneau d'administration),
tu peux demander à MedReminder de te rappeler *au moment où la dose est
due*. Coche **Me rappeler à l'heure de la prise** dans le formulaire
d'ajout ou de modification du médicament. L'option n'est disponible que
lorsque le médicament a au moins un créneau avec un horaire et qu'il
reste du stock ; sinon elle reste grisée.

Lorsqu'elle est activée, à chaque heure de créneau MedReminder affiche
une notification sur le bureau (« Il est temps de prendre … »). Si tu
as configuré les notifications par e-mail et sélectionné le canal
e-mail pour ce médicament, le même rappel est aussi envoyé par e-mail.

Quelques détails utiles à connaître :

- **Un rappel par créneau et par jour.** Chaque créneau avec horaire se
  déclenche au plus une fois pour un jour calendaire donné, même si
  l'application est redémarrée.
- **Fenêtre de tolérance.** Si l'application ne tourne pas exactement à
  l'heure du créneau — par exemple si l'ordinateur était en veille — le
  rappel se déclenche quand même à la prochaine vérification de
  l'application, à condition d'être dans les 30 minutes suivant l'heure
  du créneau. Au-delà de cette fenêtre, la dose est considérée comme
  manquée et aucun rappel n'est affiché ; MedReminder ne tient pas de
  journal des doses manquées et ne donne jamais de conseil clinique.
- **Un stock à zéro désactive le rappel.** Quand le stock atteint zéro,
  aucun rappel n'est envoyé, car il n'y a plus rien à prendre.
- **Heure d'été.** La nuit du passage à l'heure d'été, un créneau qui
  tombe dans l'heure sautée ne se déclenche pas (cette heure n'existe
  pas). La nuit du passage à l'heure d'hiver, le créneau se déclenche
  une seule fois, comme d'habitude.

Ce rappel n'est qu'une invite pratique. Il n'enregistre pas si tu as
pris la dose et ne modifie pas le stock : pour cela, utilise
*Enregistrer la prise*.

## Catalogue de référence (multi-pays)

MedReminder embarque deux instantanés d'un catalogue de médicaments
de référence et s'en sert pour auto-compléter le formulaire de
médicament.

- Dans les champs **Nom commercial** et **Principe actif**,
  commence à taper pour voir les correspondances. Sélectionner une
  ligne remplit aussi l'autre champ (et, en arrière-plan, le code
  national et le code ATC), ce qui évite de tout saisir à la main.
- La liste déroulante affiche au maximum 20 lignes et se met à jour
  environ 150 ms après la dernière frappe. Un pastille rouge à côté
  d'une ligne signifie que le produit est **suspendu ou retiré du
  marché** : tu peux quand même le choisir, MedReminder ne fait que
  signaler l'état.
- **Médicament absent du catalogue ?** Continue simplement à taper
  ce que tu veux. Si tu ne sélectionnes aucune ligne de la liste,
  MedReminder enregistre le texte tel quel et aucun lien vers le
  catalogue n'est mémorisé — le rappel fonctionne exactement comme
  avant.
- Le **pays de référence** se choisit dans *Paramètres → Général →
  Pays de référence*. Par défaut : Italie ; un changement prend
  effet à la prochaine ouverture du formulaire de médicament. Le pays
  vaut pour toute l'installation : seul un administrateur peut le
  modifier.

### Médicaments en autorisation centralisée UE

Certains médicaments sont autorisés dans toute l'Union européenne
via la *procédure centralisée*, gérée par l'Agence européenne des
médicaments (EMA). MedReminder embarque le catalogue EMA EPAR —
*European public assessment reports* — et affiche ces médicaments
dans la même liste déroulante d'auto-complétion.

- Si ton **pays de référence est un État membre de l'UE** (par
  exemple l'Italie par défaut, ou un autre pays UE choisi dans
  Paramètres), l'auto-complétion affiche **ton catalogue national
  + les médicaments centralisés valables dans toute l'UE**,
  mélangés dans la même liste. Rien à changer : les lignes UE
  apparaissent d'elles-mêmes quand elles correspondent.
- Si tu règles le **pays de référence sur `EU`**, l'auto-complétion
  n'affiche **que** les médicaments centralisés UE, sans les lignes
  nationales. Utile lorsque tu veux spécifiquement parcourir ou
  lier un produit à son autorisation EMA.
- Un médicament UE et un produit national équivalent peuvent
  apparaître en même temps dans la liste ; les deux lignes ne sont
  pas dédoublonnées. Choisis celle qui correspond à la boîte que tu
  as en main.

### Catalogues nationaux espagnol et français

Le catalogue espagnol provient de l'AEMPS CIMA (registre
« Medicamentos ») et le catalogue français d'ANSM BDPM (*Base de
données publique des médicaments*). Dans l'auto-complétion, ils
se comportent exactement comme le catalogue italien :

- Règle **Paramètres → Général → Pays de référence** sur `ES` ou
  `FR` une fois l'instantané correspondant chargé (`ES` et `FR`
  apparaissent automatiquement dans le menu déroulant dès que
  leurs catalogues sont en base).
- L'auto-complétion liste alors **ton catalogue national + les
  médicaments centralisés UE**, mélangés dans la même liste.
  L'Espagne et la France sont des États membres de l'UE, donc
  les lignes UE sont incluses par défaut comme pour l'Italie.
- Toutes les autres règles restent identiques : sélectionne une
  ligne pour remplir les deux côtés, ou continue à saisir pour
  enregistrer une entrée en texte libre inconnue de l'application.

**Sources des données et conditions de réutilisation.** Le
catalogue italien provient des données ouvertes AIFA (Agenzia
Italiana del Farmaco), publiées sous licence Creative Commons
Attribution 4.0 International (CC BY 4.0). Le catalogue UE
provient du jeu de données EMA EPAR, réutilisé selon la note
légale de l'EMA (décision 2011/833/UE sur la réutilisation des
documents de la Commission). Le catalogue espagnol provient
d'AEMPS CIMA, réutilisé selon le régime espagnol de
réutilisation des informations du secteur public (Loi 37/2007).
Le catalogue français provient d'ANSM BDPM, réutilisé sous
Licence Ouverte Etalab 2.0. La boîte de dialogue « À propos » et
le fichier `THIRD-PARTY-NOTICES.md` à la racine de l'installation
contiennent les attributions complètes.

## Scanner le code-barres de la boîte

Lorsque le catalogue de référence est activé, la fiche du médicament
comporte un bouton **Scanner le code…** à côté du nom commercial. Il
remplit le médicament à partir du catalogue en une seule étape.

1. Cliquez sur **Scanner le code…**. Une petite fenêtre s'ouvre,
   prête à recevoir le code.
2. Scannez le code-barres de la boîte avec un lecteur de codes-barres
   USB, ou saisissez le code imprimé sous le code-barres puis appuyez
   sur **Entrée**.
3. Si le code figure dans le catalogue, la fenêtre se ferme et la
   fiche est remplie comme si vous aviez choisi la ligne dans la
   liste déroulante. Sinon, un message affiche le code lu et rien
   n'est modifié.

Remarques :

- Cliquez d'abord sur **Scanner le code…**, puis scannez. Un scan
  effectué pendant que la fiche du médicament a le focus écrit le
  code dans le champ actif.
- Sur les boîtes italiennes, le lecteur lit le **code AIC** (le
  code-barres portant le texte `A` suivi de 9 chiffres). Tout lecteur
  USB de codes 1D convient ; si le code n'est pas reconnu, activez la
  symbologie **Code 32** (Italian Pharmacode) dans les réglages du
  lecteur. Le code carré 2D (DataMatrix) nécessite un lecteur 2D et
  ne correspond souvent pas encore à une entrée du catalogue.
- Le lecteur doit utiliser la même disposition de clavier que
  Windows. Avec un clavier français (AZERTY), réglez le lecteur sur
  cette disposition, sinon le code n'est pas reconnu.
- Un lecteur qui n'envoie pas Entrée après le code fonctionne aussi :
  le code est accepté un instant après le scan.

### Avec la webcam

Pas de lecteur ? Dans la fenêtre de scan, cliquez sur **Utiliser la
webcam**. La caméra ne s'allume qu'à ce moment et s'éteint dès qu'un
code est lu, que vous cliquez sur **Utiliser le lecteur**, que vous
fermez la fenêtre ou après 30 secondes sans code.

- Tenez la boîte à 10–20 cm de la caméra, avec le code-barres dans le
  cadre en pointillés, bien éclairé et net. Les webcams d'ordinateur
  portable à mise au point fixe lisent souvent mal le fin code AIC ;
  un lecteur USB est plus fiable.
- Si Windows bloque la caméra, la fenêtre l'indique et propose
  **Ouvrir les paramètres de confidentialité** : activez **Autoriser
  les applications de bureau à accéder à votre caméra** dans
  Paramètres → Confidentialité et sécurité → Caméra, puis cliquez sur
  **Réessayer**.
- Les images de la caméra ne sont jamais enregistrées ni envoyées ;
  seul le code lu est utilisé.

### Réapprovisionner en scannant

Quand vous achetez une nouvelle boîte d'un médicament déjà dans votre
liste, utilisez **Stock → Réapprovisionner par code-barres…**. Inutile
de sélectionner le médicament avant : le scan le trouve.

1. Scannez la boîte avec le lecteur ou la webcam, comme ci-dessus.
2. Le médicament qui a ce code est sélectionné et la fenêtre
   **Mouvement de stock** s'ouvre, réglée sur nouvelle boîte, avec la quantité de sa dernière nouvelle
   boîte déjà remplie. Vérifiez-la et confirmez.

- Si plusieurs médicaments ont le même code, choisissez celui à
  réapprovisionner.
- Si aucun médicament n'a ce code mais que le catalogue le connaît,
  vous pouvez l'ajouter comme nouveau médicament, ou lier le code à un
  médicament de votre liste qui n'a pas encore de code (par exemple
  saisi à la main) ; la boîte est alors ajoutée à ce médicament.
- Le médicament est trouvé par son code AIC. Une boîte qui ne porte
  que le code 2D carré (DataMatrix) n'est pas reconnue : utilisez
  **Stock → Ajouter boîte**.

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

## Compter le stock

Lorsque les comprimés dans l'armoire ne correspondent plus au stock
affiché par l'application, comptez-les et laissez l'application
enregistrer la correction :

1. Sélectionnez le médicament.
2. Menu **Stock → Compter le stock…**.
3. Saisissez la quantité comptée. La boîte de dialogue affiche le
   stock attendu (consommations automatiques mises à jour), l'écart
   de stock avec son signe et la date d'épuisement avant et après la
   correction.
4. Dans **Déjà pris aujourd'hui**, indiquez la part de la quantité
   prévue aujourd'hui déjà prise au moment du comptage. L'application
   propose les doses dont l'heure est passée ; sans heures définies,
   elle propose 0. Si vous indiquez une partie seulement, la liste
   affiche le stock en début de journée jusqu'à l'enregistrement de la
   consommation du jour.
5. Ajoutez une note si besoin (par défaut : « Comptage du stock ») et
   confirmez avec **Enregistrer le comptage**.

**Effet** : une seule correction, positive ou négative, est
enregistrée afin que le stock soit égal à la quantité comptée. Un
écart nul n'enregistre rien. Une correction positive qui ramène le stock au-dessus du seuil
d'alerte fait avancer l'époque, afin qu'une nouvelle alerte de stock
bas puisse être envoyée plus tard ; une correction négative ne la
fait jamais avancer. L'écart est une
donnée de stock uniquement : il n'est pas interprété comme des doses
oubliées ou supplémentaires.

## Modifier ou désactiver un médicament

- **Modifier** : double-clic sur la ligne, ou barre d'outils →
  **Modifier**. Tu peux changer le nom, le principe actif, le
  conditionnement, l'unité, le seuil, le médecin, les notes, la
  date de fin, les canaux de notification, et l'état Actif/Inactif.
  **La dose et la fréquence NE se modifient PAS d'ici** : utilise
  *Barre d'outils → Changer la posologie* (voir *Schémas complexes*
  plus haut).
- **Désactiver** : barre d'outils → **Désactiver**. Le médicament
  disparaît des contrôles automatiques et des alertes, mais les
  données historiques (mouvements, notifications) restent en base
  pour audit.
- **Afficher les médicaments désactivés** : les médicaments
  désactivés sont masqués dans la liste. **Traitement → Afficher les
  médicaments désactivés** les affiche de nouveau, avec l'état
  *Désactivé*, jusqu'à la fermeture de l'application. La barre d'état
  indique combien sont masqués.
- **Réactiver** : affiche les médicaments désactivés, puis
  **Modifier** → coche **Actif**. Les jours où le médicament était
  désactivé ne sont pas comptés comme consommation.
- **Supprimer** : **Traitement → Supprimer…** supprime définitivement
  un médicament saisi par erreur, avec sa posologie. C'est possible
  seulement tant que rien n'a été enregistré : aucune entrée de stock
  (quantité initiale comprise), prise, comptage ou suspension. Sinon,
  désactive-le, ou supprime d'abord ces entrées dans
  **Stock → Historique…**. Si la synchronisation est activée, le
  médicament est aussi supprimé des autres appareils, avec ce qui y a
  été enregistré entre-temps.

## Historique du stock et suppression d'une entrée erronée

**Stock → Historique…** (Ctrl+H) liste ce que tu as saisi pour le
médicament sélectionné, du plus récent au plus ancien : nouvelles
boîtes et corrections, prises, comptages et suspensions.

- **Supprimer** retire une entrée erronée ; le stock et la
  consommation sont recalculés.
- Seules les entrées enregistrées après le dernier comptage peuvent
  être supprimées : un comptage inclut déjà les erreurs antérieures,
  donc pour les corriger compte à nouveau le stock.
- Les entrées enregistrées avant l'installation de cette version ne
  peuvent pas être supprimées : corrige-les avec une correction.

## Chronologie du traitement

**Traitement → Chronologie du traitement…** (Ctrl+T) ou le bouton
**Chronologie** de la barre d'outils ouvre une vue en lecture seule
avec une ligne par médicament et les jours sur l'axe horizontal. Par
défaut, elle affiche 60 jours en arrière et 120 jours en avant.

- **Barre pleine** : traitement actif, de la date de début à la date de
  fin (ou jusqu'au bord de la vue s'il n'y a pas de date de fin).
- **Barre hachurée à bordure pointillée** : suspension, prévue ou en
  cours.
- **Losange plein** : une nouvelle dose, fréquence ou posologie entre en
  vigueur. **Losange vide** : palier suivant d'une décroissance par
  paliers.
- **Triangle sur une ligne verticale** : date d'épuisement estimée.
  C'est la même estimation que la colonne *Épuisement* : stock actuel
  divisé par la quantité quotidienne d'aujourd'hui. Elle ne tient pas
  compte des suspensions ou changements de posologie futurs.
- **Ligne verticale pointillée** : aujourd'hui.
- Un médicament désactivé apparaît en gris ; sa barre s'arrête à
  aujourd'hui car la date de désactivation n'est pas enregistrée.

Commandes :

- **Avant** / **Après** déplacent la période de 30 jours,
  **Aujourd'hui** rétablit la période par défaut. Dans le graphique,
  les flèches gauche et droite (ou Maj + molette de la souris) la
  déplacent d'une semaine.
- Les flèches haut et bas sélectionnent un médicament ; la zone
  **Détails** sous le graphique donne les mêmes informations sous forme
  de texte (posologie, stock, épuisement estimé, suspensions et
  changements de posologie dans la période). Le survol d'un élément
  affiche le même texte en info-bulle.
- **Afficher dans la liste** (ou Entrée, ou un double clic) ferme la
  chronologie et sélectionne le médicament dans la liste principale, où
  s'appliquent les actions habituelles.

La chronologie ne modifie aucune donnée. Les dates d'épuisement sont
des estimations : elles servent à planifier les réapprovisionnements,
pas d'avis clinique.

## Fiche de traitement (impression et PDF)

**Traitement → Fiche de traitement…** (Ctrl+P) ou le bouton **Fiche
traitement** de la barre d'outils ouvre une fiche des médicaments
actifs à remettre au médecin traitant, aux urgences ou au pharmacien.
Pour chaque médicament actif, elle indique le principe actif, la
posologie (créneaux de prise, ou dose × fois par jour), la période de
traitement et le médecin. Les médicaments désactivés n'y figurent pas.

- **Inclure les notes** : désactivé par défaut. Les notes sont du
  texte libre et peuvent être privées ; cochez la case seulement si la
  fiche doit les contenir.
- **Papier** : A4 ou Letter (US), présélectionné selon la région de
  Windows.
- **Imprimer…** ouvre l'aperçu du tableau. Les listes longues
  continuent sur la page suivante, avec l'en-tête et les titres de
  colonnes répétés.
- **Enregistrer en PDF…** demande où enregistrer le fichier et écrit
  le même tableau en PDF via l'imprimante Windows « Microsoft Print to
  PDF ». Si cette imprimante a été supprimée, la boîte de dialogue
  explique comment la rajouter (Panneau de configuration → Programmes
  → Activer ou désactiver des fonctionnalités Windows).
- **Enregistrer dans un fichier…** et **Copier dans le presse-papiers**
  conservent la version texte.

Le PDF et le fichier texte sont écrits uniquement à l'endroit que vous
choisissez ; MedReminder n'en garde aucune copie. Chaque page porte la
mention que MedReminder est un rappel organisationnel, pas un
dispositif médical.

## Profils multiples et rôles administrateur/utilisateur

MedReminder peut gérer les médicaments de **plusieurs personnes**
depuis le même compte Windows — cas typique : un parent qui suit
son propre traitement et celui d'un ou deux proches. Chaque profil
a sa propre base de données et son propre destinataire e-mail ; le
serveur SMTP, le dossier de sauvegarde automatique et le registre
des profils sont partagés et gérés par un profil
**administrateur**.

### Rôles

- **Administrateur** — gère les paramètres globaux (SMTP,
  Sauvegarde, liste des profils, PIN de n'importe quel profil) en
  plus de ses propres données. Il doit toujours exister au moins
  un administrateur.
- **Utilisateur** — gère uniquement son propre profil (médicaments,
  stock, thérapies, destinataire e-mail personnel). Ne voit pas
  l'onglet SMTP ni l'onglet Sauvegarde dans les Paramètres, et ne
  voit pas `Outils → Gérer les profils…`.

Le rôle est choisi à la création du profil. Un administrateur peut le
changer ensuite : `Outils → Gérer les profils…`, sélectionne le
profil, **Changer le rôle…**. Le rôle du profil ouvert ne peut pas
être modifié : ouvre d'abord un autre profil administrateur. Il reste
toujours au moins un administrateur. Avant de rendre administrateur un
profil sans PIN, pense à en définir un : sinon toute personne devant
le PC pourrait l'ouvrir.

Le rôle est une barrière « douce » : quiconque a accès au système
de fichiers peut modifier `profiles.json` à la main et devenir
administrateur. L'interface respecte le rôle, pas le système de
fichiers.

### Créer d'autres profils (administrateur)

1. `Outils → Gérer les profils…` — cette entrée n'existe que pour
   les administrateurs.
2. **Nouveau profil** → saisis un nom, choisis Administrateur ou
   Utilisateur (par défaut : Utilisateur), définis éventuellement
   un PIN. Confirme.
3. Le nouveau profil apparaît immédiatement dans le sélecteur au
   prochain lancement.

### Changer de profil

`Fichier → Changer de profil…` ouvre le sélecteur. Choisis le
profil cible et confirme : l'application redémarre automatiquement
pour que le nouveau profil soit complètement isolé. Si le profil
choisi a un PIN, la demande apparaît avant l'ouverture de
l'application.

### Renommer, changer le PIN, supprimer

`Outils → Gérer les profils…` (administrateur uniquement) propose
aussi :

- **Renommer** — uniquement le nom affiché. L'identifiant interne
  ne change jamais.
- **Changer le PIN** — définir, changer ou effacer le PIN de
  n'importe quel profil.
- **Supprimer** — demande de **saisir le nom du profil** pour
  confirmer. Une case séparée permet de supprimer aussi les
  données du profil sur le disque ; elle est désactivée par
  défaut, ainsi le dossier reste disponible pour une récupération
  manuelle.

Le profil actif ne peut pas être supprimé (change d'abord de
profil), ni le dernier administrateur restant.

### À propos du PIN

Le PIN est une **friction, pas une sécurité**. Il empêche les
changements de profil accidentels, mais **ne** chiffre pas les
données — quiconque a accès à ce PC peut toujours ouvrir les
fichiers du profil. Trois tentatives incorrectes ferment la
demande et l'application.

**Une vraie séparation exige des comptes Windows distincts.** Chaque
compte Windows a son propre dossier `%LOCALAPPDATA%\MedReminder\`, que
les autres utilisateurs Windows standard (non administrateurs) ne
peuvent pas lire. Les profils d'un même compte Windows sont une
commodité, pas une barrière de confidentialité : quiconque utilise ce
compte peut lire les fichiers de tous les profils, et les sauvegardes
automatiques configurées par l'administrateur incluent tous les
profils, y compris ceux protégés par un PIN.

Si tu oublies un PIN, supprime-le à la main dans
`%LOCALAPPDATA%\MedReminder\profiles.json` (efface `PinHash` et
`PinSalt` et mets `PinIterations` à `0` pour l'entrée concernée).
Ce fonctionnement est documenté plutôt que corrigé par un flux
« réinitialiser le PIN » exprès : la récupération n'est pas un
bug, parce que le PIN n'est pas une sécurité.

### Disposition sur le disque

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                        ← registre des profils
├── smtp.settings.json                   ← SMTP partagé (admin)
├── smtp.protected                       ← mot de passe chiffré DPAPI
├── backup.settings.json                 ← config sauvegarde partagée (admin)
├── backup.state.json                    ← état de la dernière sauvegarde
├── logs\medreminder-YYYYMMDD.log
└── profiles\
    ├── <id-profil>\                     ← un dossier par profil
    │   ├── medreminder.db (+ -wal, -shm)
    │   ├── notifications.settings.json  ← ToAddress de ce profil
    │   └── ui.settings.json             ← taille du texte de ce profil
    └── …
```

### La sauvegarde automatique couvre tous les profils

Quand la sauvegarde automatique est activée, chaque exécution
journalière sauvegarde la base de données de **tous** les profils
dans le dossier partagé, sous des noms de la forme
`medreminder-<id-profil>-YYYYMMDD-HHmmss.db`. La rétention est
appliquée par profil : la sauvegarde la plus récente d'un profil
ne protège pas les sauvegardes plus anciennes d'un autre.

Lors de la restauration depuis `Paramètres → Sauvegarde → Restaurer
sauvegarde…`, la boîte de dialogue demande quel profil doit
recevoir la base importée. Par défaut, elle sélectionne le profil
référencé dans le nom du fichier. Si tu restaures dans un profil
autre que l'actif, l'application ne redémarre pas ; si tu
restaures dans le profil actif, elle redémarre pour ouvrir
proprement la nouvelle base.

### Démarrage automatique avec Windows

L'entrée de démarrage automatique de Windows est unique par
utilisateur Windows. À la connexion, l'application ouvre le
**dernier** profil sans afficher le sélecteur ; si ce profil a un
PIN, la demande s'affiche par-dessus la fenêtre vide. Pour ouvrir
un profil différent au démarrage automatique, utilise
`Fichier → Changer de profil…` une fois l'application ouverte.

### Mise à niveau depuis une installation mono-utilisateur

Si tu as déjà un fichier `medreminder.db` sous
`%LOCALAPPDATA%\MedReminder\` d'une version antérieure,
l'application exécute une **migration V1 → V2** au prochain
lancement :

1. Elle effectue une sauvegarde obligatoire dans
   `%LOCALAPPDATA%\MedReminder\backups\pre-migration-YYYYMMDD-HHmmss\`
   contenant le `medreminder.db` original (et ses fichiers
   associés) et le `smtp.settings.json` original.
2. Elle déplace la base dans `profiles\default\medreminder.db` et
   crée le `profiles.json` initial avec un seul profil
   administrateur nommé `User`.
3. Elle extrait le destinataire (`Smtp.ToAddress`) de
   `smtp.settings.json` vers
   `profiles\default\notifications.settings.json`.

La migration est **atomique** — si une étape échoue après le
pré-backup, l'application revient à l'état V1 et conserve la
sauvegarde de pré-migration.

Le **backup de pré-migration n'est pas nettoyé automatiquement** :
après avoir vérifié que l'application migrée ouvre les mêmes
données, tu peux supprimer manuellement le dossier
`backups\pre-migration-*`. Renomme le profil `User` comme tu
préfères depuis `Outils → Gérer les profils… → Renommer`.

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

## Notifications au soignant

**Paramètres → Notifications → E-mail soignant (facultatif)**.

Un profil peut désigner un second destinataire — par exemple un membre
de la famille ou un soignant qui gère le renouvellement d'ordonnance à
ta place. Lorsque ce champ est renseigné, chaque e-mail envoyé au
destinataire principal est également envoyé au soignant, dans le
**même** message. Rien d'autre ne change : le transport, le contenu du
message et les moments d'envoi sont exactement les mêmes qu'avant.

- **Pour l'activer** : saisis l'adresse e-mail du soignant et
  enregistre.
- **Pour le désactiver** : vide le champ et enregistre. Un champ
  vide signifie qu'aucun soignant n'est configuré — comportement par
  défaut.
- **Les deux adresses sont visibles aux deux destinataires** : le
  soignant et le destinataire principal peuvent voir l'adresse de
  l'autre sur l'e-mail. C'est intentionnel pour qu'une réponse
  parvienne à tous.
- L'adresse du soignant ne peut pas être identique à celle du
  destinataire principal et doit être une adresse e-mail valide ;
  sinon l'enregistrement est rejeté avec un message.

Le réglage est par profil : le soignant d'un profil n'est pas le
soignant d'un autre profil.

## Demander une ordonnance au médecin

Sélectionne un médicament et choisis **Traitement → Demander une
ordonnance…** (ou le bouton **Demander ordonnance** de la barre
d'outils). L'action est disponible pour chaque médicament, quel que
soit son stock.

MedReminder prépare un court message avec le nom du médicament, le
conditionnement, le code produit (lorsque le médicament est lié au
catalogue) et le nom du profil en signature. Si le médicament a un
médecin de référence, la formule d'appel utilise ce nom. La posologie,
les notes et les autres détails cliniques ne sont pas inclus. L'objet
et le message sont modifiables avant l'envoi.

Trois façons de transmettre le message :

- **Copier** : l'objet et le message vont dans le presse-papiers, à
  coller dans un webmail, une application de messagerie ou un portail
  patient.
- **Ouvrir dans la messagerie** : ouvre un nouvel e-mail déjà rempli
  dans le logiciel de messagerie par défaut. Si le message est trop
  long pour la messagerie, ou si aucune messagerie n'est configurée, il
  est copié dans le presse-papiers à la place.
- **Envoyer…** : envoie le message via le compte e-mail configuré dans
  **Paramètres → E-mail SMTP**, après une confirmation explicite.
  Disponible uniquement lorsque l'envoi SMTP est configuré et que
  l'e-mail du médecin est défini.

L'e-mail du médecin se définit dans **Paramètres → Notifications →
E-mail du médecin (facultatif)**. Il est propre au profil, inclus dans
l'export et l'importation, et utilisé uniquement pour les demandes que
tu envoies toi-même : les notifications automatiques ne sont jamais
envoyées à cette adresse.

MedReminder n'envoie jamais une demande d'ordonnance de lui-même. Le
contenu du message et le destinataire ne sont pas écrits dans les
fichiers journaux.

## Configurer le démarrage automatique

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

## Export et importation

En plus de la sauvegarde brute de la base de données, MedReminder peut
produire un **fichier chiffré et portable** unique avec toutes tes
données. Contrairement à une sauvegarde ordinaire, ce fichier n'est pas
lié à ton compte Windows ni à ton PC — c'est donc le moyen recommandé
pour transférer MedReminder vers un nouvel ordinateur.

**Paramètres → Sauvegarde → Exporter toutes les données (chiffré)…** :

- Choisis où enregistrer le fichier (extension `.mrz`).
- Choisis une **phrase secrète** (au moins 12 caractères) et
  saisis-la deux fois.
- Active facultativement les paramètres partagés à inclure :
  paramètres SMTP, mot de passe SMTP, préférences de sauvegarde,
  préférences utilisateur (langue et pays de référence du catalogue).
  Tous sont désactivés par défaut. Si tu inclus le mot de passe SMTP,
  il est chiffré de nouveau avec ta phrase secrète — il n'est jamais
  écrit en clair.
- Clique sur **Exporter**.

**Administrateur : tous les profils en une fois.** Lorsqu'il existe
plus d'un profil, un profil administrateur voit aussi **Exporter tous
les profils**. Choisis un dossier au lieu d'un fichier : MedReminder
écrit un fichier chiffré par profil
(`medreminder-export-<profileId>-<timestamp>.mrz`), tous avec la même
phrase de passe. Pour restaurer un profil, ouvre ce profil et importe
son fichier.

**La phrase secrète ne peut pas être récupérée.** Il n'existe ni
réinitialisation, ni porte dérobée, ni copie sur serveur. Si tu perds
la phrase secrète, le fichier ne pourra plus jamais être lu — conserve-la
en lieu sûr.

**Paramètres → Sauvegarde → Importer depuis une exportation…** :

- Sélectionne le fichier `.mrz`. MedReminder affiche son contenu
  (version, date, portée, paramètres inclus) avant toute opération.
- Saisis la phrase secrète.
- Coche **« Je comprends que ceci remplacera les données du profil
  actuel. »** L'importation remplace entièrement les données du profil
  actuel — il n'y a pas de mode fusion. Une copie de sécurité de la
  base de données actuelle est conservée sous le nom
  `medreminder.db.bak-<horodatage>`.
- Si le fichier a été exporté depuis un autre profil, MedReminder
  demande une confirmation : l'importer remplace les données du profil
  actif par celles de l'autre profil.
- Clique sur **Importer**, puis **redémarre** MedReminder quand c'est
  demandé, afin que les données importées soient chargées proprement.

Si la phrase secrète est incorrecte, si le fichier est endommagé ou
s'il a été produit par une version plus récente de MedReminder,
l'importation s'arrête avec un message clair et tes données actuelles
restent intactes.

Le format de l'archive est documenté publiquement dans
[`docs/EXPORT-FORMAT.md`](EXPORT-FORMAT.md), ainsi tes données ne sont
jamais enfermées — elles peuvent être déchiffrées avec des outils
standards si nécessaire.

## Sauvegarde vers un dossier cloud

MedReminder peut aussi écrire la sauvegarde automatique quotidienne
sous forme d'**instantané chiffré** dans un dossier local que ton
système d'exploitation synchronise déjà (OneDrive, iCloud Drive,
Dropbox, Google Drive Desktop, …). C'est le moyen peu coûteux de
transférer tes données d'un « PC de la maison » vers un « PC du
bureau » sans serveur, et cela garde une copie hors de la machine en
cas de panne du disque.

**Ce n'est pas une synchronisation en temps réel.** MedReminder écrit
au plus un instantané par jour, et un seul ordinateur à la fois
devrait écrire. Si tu modifies des médicaments sur deux appareils
entre deux instantanés, les deux copies divergent — et la
restauration suivante efface les données de la machine sur laquelle
tu restaures. Décide à l'avance quel appareil est « actif » et ne
restaure sur l'autre que lorsque tu changes.

### Configuration sur le premier appareil

**Paramètres → Sauvegarde → Sauvegarde vers un dossier synchronisé
(chiffrée)** :

- Coche la case.
- Choisis un dossier à l'intérieur du dossier de synchronisation
  local de ton service cloud (par exemple
  `C:\Users\<nom>\OneDrive\MedReminder`). MedReminder ne communique
  jamais lui-même avec OneDrive / iCloud / Dropbox — il écrit
  seulement les fichiers à cet endroit, et l'agent de synchronisation
  du système les envoie.
- Indique le nombre d'instantanés à conserver (par défaut : 30).
- Clique sur **Définir / changer…** à côté de Phrase de passe de
  sauvegarde et choisis une phrase de passe (au moins 12 caractères).
  Cette phrase de passe ne quitte jamais la machine.
- Enregistre.

À partir du passage quotidien suivant, MedReminder écrit
`medreminder-<profileId>-<timestamp>.mrz` dans le dossier. Le fichier
est chiffré avec une clé dérivée de ta phrase de passe de
sauvegarde ; le service cloud ne voit jamais tes données en clair.

Un instantané est écrit pour **chaque profil** présent sur
l'ordinateur, comme pour la sauvegarde locale, et tous sont chiffrés
avec la même phrase de passe de sauvegarde. Quiconque connaît la
phrase de passe peut donc lire les données de tous les profils, y
compris ceux protégés par un PIN.

### Configuration sur le second appareil

- Installe MedReminder.
- **Paramètres → Sauvegarde → Définir / changer…** et saisis la
  **même** phrase de passe de sauvegarde que sur le premier appareil.
  C'est la seule étape incontournable : sans la même phrase de passe,
  la seconde machine ne peut pas déchiffrer ce que la première a
  écrit.
- L'instantané automatique quotidien reste désactivé sur le second
  appareil — il n'est utile que sur une seule machine.

### Restauration sur le second appareil

**Paramètres → Sauvegarde → Restaurer depuis le dossier cloud…** :

- Indique dans la fenêtre le dossier de synchronisation local (celui
  dans lequel écrit le premier appareil).
- Choisis l'instantané le plus récent dans la liste. Chaque ligne
  affiche la date, le nom du profil (ou son identifiant, si le profil
  n'existe pas sur cet ordinateur) et un court « hash de l'appareil »
  pour distinguer les instantanés provenant de machines différentes.
  Le hash de l'appareil est une empreinte SHA-256 du nom d'hôte de la
  machine d'origine — assez pour regrouper les instantanés par
  provenance, pas assez pour identifier la machine.
- L'instantané le plus récent du profil actif est présélectionné. La
  restauration écrase toujours le profil **actif** : pour restaurer un
  autre profil, bascule d'abord sur celui-ci. Si tu choisis
  l'instantané d'un autre profil, MedReminder demande une confirmation
  avant de remplacer par celui-ci les données du profil actif.
- Coche **« Je comprends que cela écrasera les données du profil
  courant. »** — la restauration se fait uniquement par écrasement.
- Clique sur **Restaurer**. MedReminder déchiffre l'instantané,
  remplace la base de données du profil courant et te propose de
  redémarrer.

### Remarques

- **Perdre la phrase de passe, c'est perdre les données.** Il n'y a
  pas de réinitialisation. La phrase de passe est stockée localement,
  chiffrée avec les identifiants de ton compte Windows ; elle ne
  quitte jamais la machine et n'apparaît jamais dans le cloud.
- L'instantané automatique quotidien n'inclut **pas** le mot de passe
  SMTP ni tes préférences utilisateur — pour cela, utilise l'export
  chiffré ponctuel décrit plus haut, avec les cases des paramètres
  partagés.
- La conservation de MedReminder supprime les anciens fichiers
  uniquement du dossier visible. Ton service cloud conserve
  probablement les fichiers supprimés dans sa propre corbeille
  (OneDrive : 30 jours par défaut) — MedReminder ne peut pas la vider
  à ta place, et n'essaie pas.
- Ne place **pas** le fichier de base de données en cours
  d'utilisation dans un dossier synchronisé. Seuls les instantanés
  chiffrés `.mrz` y ont leur place.

### OneDrive au lieu d'un dossier

Dans la même section, **Stockage** peut être réglé sur **OneDrive
(dossier d'application)** : cliquez sur **Se connecter…**, connectez-vous
avec un compte Microsoft, définissez la phrase secrète de sauvegarde et
enregistrez. Les sauvegardes vont dans `Apps/MedReminder26/backups` dans
OneDrive, chiffrées comme ci-dessus ; aucun dossier local n'est
nécessaire. **Restaurer depuis le dossier cloud…** liste alors les sauvegardes
OneDrive par date et profil et ne télécharge que celle que vous
restaurez.

**Google Drive (dossier MedReminder/backups)** fonctionne de la même
façon avec un compte Google : les sauvegardes vont dans un dossier
visible **MedReminder → backups** de votre Mon Drive, chiffrées comme
ci-dessus, et **Restaurer depuis le dossier cloud…** les liste depuis
cet emplacement.

## Synchronisation entre PC

Plusieurs PC peuvent tenir le même profil à jour : ce que vous saisissez sur
l'un apparaît sur les autres. Les PC n'échangent que des modifications
chiffrées par un dossier partagé (un dossier OneDrive, Google Drive ou
Dropbox synchronisé par son application de bureau, ou un partage réseau).
Aucun serveur n'intervient et le dossier ne contient jamais de données
lisibles.

Ouvrez **Outils → Synchronisation…** depuis un profil administrateur (les autres
profils ne la voient pas). Chaque profil synchronisé a son propre
groupe, sa clé et ses appareils ; la fenêtre gère le groupe du profil
ouvert.

### OneDrive ou dossier partagé

Lorsque vous activez la synchronisation ou rejoignez un groupe,
MedReminder demande où conserver le groupe :

- **OneDrive** : connectez-vous avec un compte Microsoft dans la fenêtre
  du navigateur qui s'ouvre. MedReminder ne peut utiliser que son propre
  dossier d'application (`Apps/MedReminder26` dans OneDrive) ; les
  données y sont chiffrées. Chaque PC se connecte avec le **même** compte
  Microsoft. L'application OneDrive sur le PC n'est pas nécessaire.
- **Google Drive** : connectez-vous avec un compte Google dans la fenêtre
  du navigateur qui s'ouvre. Les données de synchronisation vont dans le
  dossier de données masqué de MedReminder dans Google Drive (il
  n'apparaît pas dans Mon Drive), chiffrées. Chaque PC se connecte avec
  le **même** compte Google.
- **Un dossier partagé** : un dossier synchronisé par un autre programme,
  ou un partage réseau, comme décrit ci-dessous.

Si la session OneDrive ou Google Drive expire (changement de mot de
passe, longue inactivité), l'état l'indique et **Se reconnecter à
OneDrive** (ou **Se reconnecter à Google Drive**) reprend la
synchronisation ; les modifications faites entre-temps sont envoyées
ensuite.

### Activer sur le premier PC

1. **Activer la synchronisation…**, choisissez le dossier partagé.
2. Saisissez un nom pour ce PC et une **phrase secrète de synchronisation**
   (au moins 10 caractères, saisie deux fois). Ce n'est pas la phrase
   secrète des sauvegardes. Conservez-la : sans elle, les données du dossier
   sont illisibles, et elle ne peut pas être récupérée.

### Rejoindre depuis un autre PC

1. Attendez que le client de synchronisation ait téléchargé le dossier
   partagé.
2. **Rejoindre un groupe…**, choisissez le même dossier, saisissez un nom
   pour ce PC et la même phrase secrète.
3. Confirmez : **les données de ce profil sur ce PC sont remplacées** par
   celles du groupe (une copie est conservée à côté de la base).
   MedReminder redémarre.

Si la phrase secrète ouvre plus d'un groupe dans le dossier ou le compte
(plusieurs profils synchronisés avec la même phrase secrète),
MedReminder demande lequel rejoindre et affiche chaque groupe avec ses
appareils.

Avec OneDrive ou Google Drive, l'opération peut prendre jusqu'à une
minute : MedReminder attend que le compte liste le nouveau PC, afin que
les autres PC conservent les modifications dont il a encore besoin.

### Utilisation courante

- MedReminder synchronise quelques secondes après chaque modification,
  toutes les 5 minutes et avec **Synchroniser maintenant**.
- L'onglet **Appareils** liste les PC du groupe et leur dernier contact.
- Si deux PC modifient la même chose avant d'avoir vu la modification de
  l'autre, la plus récente est conservée et le cas apparaît dans
  **Conflits**. Pour un champ de médicament, **Restaurer la valeur perdue**
  remet l'autre valeur ; **Ignorer** retire l'entrée de la liste.
- Importer un export ou restaurer une sauvegarde sur un profil synchronisé
  démarre une nouvelle **génération** : après un avertissement, les autres
  PC abandonnent ce qu'ils n'avaient pas encore envoyé et doivent être
  reconstruits avec **Reconstruire depuis le groupe…**.
- Le **nom du profil** et les **destinataires des notifications**
  (Paramètres → Notifications) appartiennent au groupe : une
  modification sur un PC parvient aux autres, et un PC qui rejoint le
  groupe prend ceux du groupe. Pour renommer un autre profil
  synchronisé, ouvrez d'abord ce profil.
- **E-mail** : chaque PC où l'e-mail est configuré (Paramètres → E-mail
  SMTP) envoie ses propres messages de stock bas et au soignant ; avec
  deux PC synchronisés, le même message arrive donc deux fois.
  Configurez l'e-mail sur un seul PC du groupe.
- **Désactiver la synchronisation…** arrête la synchronisation sur ce PC et
  conserve ses données.

### Codes d'association

Un PC déjà dans le groupe peut afficher un **code d'association** :
**Associer un appareil…** affiche un code QR (pour la future application
mobile) et le même code sous forme de texte. Sur un autre PC,
**Rejoindre avec un code d'association…** utilise ce code à la place de
la phrase secrète ; le PC doit tout de même accéder au même compte ou
au même dossier.

- Le code est valable 10 minutes et seulement tant que sa fenêtre reste
  ouverte. Fermer la fenêtre le retire.
- Quiconque lit le code pendant sa validité peut lire les données du
  groupe : ne le montrez qu'à vos propres appareils, ne l'envoyez ni par
  message ni par e-mail. Le code reste masqué jusqu'à ce que vous
  choisissiez **Afficher le code** : affichez-le seulement quand l'autre
  appareil est prêt, car à partir de là quiconque voit ou enregistre
  l'écran (assistance à distance comprise) peut l'utiliser.

### Changer la clé, retirer un PC perdu

- Onglet **Appareils** → sélectionnez un PC → **Retirer l'appareil…** :
  pour un PC perdu ou volé. **Changer la clé et la phrase secrète…** fait
  de même sans désigner de PC, par exemple si la phrase secrète est
  connue de quelqu'un.
- Vous choisissez une **nouvelle phrase secrète de synchronisation**. Le
  groupe reçoit une nouvelle clé ; le PC retiré ne la reçoit pas et ne
  peut rien lire de ce qui est écrit ensuite. Ce qu'il détenait déjà lui
  reste lisible.
- Fermez aussi les sessions du PC perdu dans les paramètres de sécurité
  du compte Microsoft ou Google : jusque-là, il peut encore accéder au
  stockage.
- Chaque autre PC cesse d'envoyer des modifications et indique que la
  clé a changé. Utilisez-y **Saisir la nouvelle clé…**, avec la nouvelle
  phrase secrète ou un code d'association d'un PC qui a déjà la nouvelle
  clé. Le profil est reconstruit à partir du groupe et **les
  modifications faites sur ce PC sont conservées**, y compris celles
  enregistrées pendant l'attente. MedReminder redémarre.

### Installation : plusieurs appareils

**Outils → Installation…** (administrateurs uniquement) partage toute
l'installation entre appareils : profils, rôles et codes PIN, paramètres
e-mail, paramètres de sauvegarde cloud et pays de référence.

- **Publier l'installation…** sur le premier appareil : choisissez le
  stockage qui contient déjà les groupes de synchronisation des profils
  et une phrase secrète de l'installation. Cette phrase secrète est un
  secret d'administrateur : elle permet d'ajouter un appareil et de
  récupérer chaque profil quand aucun autre appareil n'est disponible.
- **Appareils → Ajouter un appareil…** : sélectionnez les profils que le
  nouvel appareil contiendra, puis saisissez sur le nouvel appareil le
  code affiché. Le code dure 10 minutes, ou jusqu'à la fermeture de la
  fenêtre. Le nouvel appareil ne reçoit que les profils sélectionnés.
- **Rejoindre une installation existante…** sur le nouvel appareil : avec
  le code, ou avec la phrase secrète de l'installation et le code PIN
  d'un administrateur de l'installation. Ses paramètres remplacent ceux
  de cet appareil, et les profils déjà présents y sont ajoutés.
  MedReminder redémarre avec les nouveaux profils. Sur un nouveau PC, la
  fenêtre d'accueil du premier démarrage propose la même chose avec
  **Rejoindre une installation existante…**.

**Appareil maître.** Un seul appareil, le maître, envoie les rappels
par e-mail et exécute la sauvegarde cloud ; les autres affichent leurs
rappels à l'écran. L'appareil qui publie l'installation est le maître.
Pour transférer le rôle, sélectionnez un appareil dans **Appareils** et
choisissez **Définir comme maître…** : le maître actuel passe le relais
à sa prochaine synchronisation, et aucun appareil n'envoie d'e-mail
entre-temps. Un maître qui n'a pas synchronisé l'installation depuis 24
heures cesse d'envoyer des e-mails ; s'il est perdu, l'appareil élu à sa
place prend le relais une heure plus tard. Les installations publiées
avant cette version n'ont pas de maître tant qu'un administrateur n'en
désigne pas un.

Le maître contrôle tous les profils qu'il contient, même ceux qui ne
sont pas ouverts, et envoie leurs rappels par e-mail aux destinataires
de chaque profil. Sur les autres appareils, une demande d'ordonnance
s'ouvre dans le client de messagerie, et le test de la connexion e-mail
n'est disponible que sur le maître.

**Retirer un appareil.** Quand un appareil est perdu, vendu ou donné,
sélectionnez-le dans **Appareils** et choisissez **Retirer
l'appareil…**, de préférence sur le maître. Vous choisissez une
nouvelle phrase secrète de l'installation : l'appareil retiré garde ce
qu'il a déjà mais ne reçoit plus rien. Chaque autre appareil s'arrête
jusqu'à ce qu'un administrateur y ouvre **Outils → Installation… →
Saisir la nouvelle clé…** et saisisse la nouvelle phrase secrète, ou un
code affiché par l'appareil qui a effectué le retrait. Les
modifications faites entre-temps sur ces appareils sont conservées. Les profils que l'appareil retiré contenait reçoivent aussi de
nouvelles clés : retirez-le depuis un appareil qui les contient tous (le
maître les contient). Les autres appareils prennent ces clés d'eux-mêmes ;
un profil ouvert à ce moment demande de redémarrer MedReminder.

Sur le nouveau maître, un administrateur termine le passage de relais :
la fenêtre s'ouvre d'elle-même, ou depuis **Outils → Installation… →
Terminer le passage de relais…**. Elle montre les paramètres de
l'installation, teste la connexion e-mail depuis cet appareil, se
connecte au stockage de la sauvegarde cloud et demande à nouveau la
phrase secrète de la sauvegarde cloud (jamais copiée entre appareils),
puis télécharge les profils dont le nouveau maître a besoin. La phrase
secrète de l'installation apporte les profils qu'aucun appareil
disponible ne contient.

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
déroulante (français, anglais, italien, espagnol ou allemand) et clique sur
**Enregistrer**. MedReminder propose de redémarrer pour appliquer
le changement.

Notes :
- Les notifications toast Windows utilisent la langue choisie ici,
  comme le reste de l'application.
- Les notifications e-mail et la fiche de traitement utilisent la
  langue sélectionnée ici.

## Taille du texte

**Paramètres → Général → Taille du texte (ce profil)** : choisis
**Normale**, **Grande** ou **Très grande** et clique sur
**Enregistrer**. MedReminder propose de redémarrer ; après le
redémarrage, toutes les fenêtres de ce profil affichent un texte, des
boutons et des lignes de liste plus grands. Le guide est aussi
agrandi.

Notes :
- La taille appartient au profil : sur un PC partagé, chaque personne
  garde la sienne. Le choix du profil et la demande de PIN, affichés
  avant l'ouverture d'un profil, utilisent toujours Normale.
- MedReminder suit aussi la mise à l'échelle de l'affichage et les
  thèmes de contraste de Windows. Avec un thème de contraste, la liste
  des médicaments n'utilise plus les lignes colorées mais les couleurs
  du thème ; la colonne **État** indique toujours la situation de
  chaque médicament.
- Sur un petit écran, une fenêtre en **Très grande** est réduite à la
  taille de l'écran et une partie peut ne pas être visible ; choisis
  alors **Grande**.

## Soutenir le développement

Si le mainteneur l'a activée, l'entrée **? → Soutenir le
développement…** ouvre une petite fenêtre où vous pouvez, de manière
totalement volontaire, contribuer au projet. C'est facultatif et jamais
nécessaire pour utiliser MedReminder.

- Choisissez un montant fixe (2 €, 5 €, 10 €, 20 €) ou, lorsqu'il est
  proposé, un **montant personnalisé**.
- Choisissez un moyen de paiement (Stripe ou PayPal).
- Cliquez sur **Continuer avec …** : MedReminder ouvre la page de
  paiement officielle du prestataire dans votre navigateur par défaut.

Avec un montant personnalisé, vous choisissez le montant exact **sur la
page du prestataire**, pas dans MedReminder. L'application ne traite
jamais le paiement elle-même, ne voit pas les données de votre carte et
ne peut pas confirmer qu'un paiement a abouti : elle ne fait qu'ouvrir
la page. Si le mainteneur n'a pas configuré cette fonction, l'entrée de
menu n'apparaît pas.

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

- Il n'enregistre pas si tu as pris une dose, ne suit pas
  l'observance thérapeutique et n'alerte pas sur les prises manquées
  (le rappel à l'heure de la prise n'est qu'une invite pratique, pas
  un système d'observance).
- Il ne fournit pas d'indications thérapeutiques ni d'interactions
  médicamenteuses.
- Il ne synchronise pas entre différents appareils.
- Il ne commande pas de médicaments automatiquement.
- Il ne contacte pas ton médecin directement.

Son unique but est de te prévenir à temps que tu dois demander une
nouvelle ordonnance.
