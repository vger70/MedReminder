# MedReminder — Guide utilisateur

MedReminder te prévient **à temps** quand un médicament va manquer,
pour que tu puisses demander une ordonnance à ton médecin avant d'en
être privé. Il peut aussi te rappeler l'heure de chaque prise, suivre
plusieurs personnes et fonctionner sur plusieurs ordinateurs.

> **MedReminder est un aide-mémoire organisationnel, pas un
> dispositif médical.** Il ne fournit ni diagnostic, ni indication
> thérapeutique, ni modification de traitement, ni conseil clinique.
> Toute décision concernant le traitement doit être prise avec ton
> médecin.

Appuie sur **F1** ou ouvre **? → Guide utilisateur** pour lire ce guide
dans l'application. L'architecture technique est décrite dans
`docs/ANALYSIS.md`.

---

## Sommaire

1. [Pour commencer](#start)
   - [Premier démarrage](#first-start) · [La fenêtre principale](#main-window) ·
     [Où trouver quoi](#where)
2. [Médicaments](#medicines)
   - [Ajouter un médicament](#add-medicine) ·
     [Horaires de prise](#slots) ·
     [Schémas complexes](#regimens) ·
     [Rappel à l'heure de la prise](#dose-reminder) ·
     [Modifier, désactiver, supprimer](#edit-medicine)
3. [Trouver un médicament : catalogue et code-barres](#catalogue)
4. [Stock](#stock)
   - [Ajouter une boîte](#add-package) · [Boîtes et péremption](#packages) ·
     [Enregistrer une prise](#intake) ·
     [Corriger le stock](#correct) · [Compter le stock](#count) ·
     [Historique](#history)
5. [Chronologie, fiche de traitement et demande d'ordonnance](#documents)
6. [Notifications et e-mail](#notifications)
   - [Paramètres e-mail des principaux fournisseurs](#smtp-providers)
7. [Plusieurs personnes : profils et rôles](#profiles)
8. [Protéger tes données : sauvegarde et export](#backup)
9. [Plusieurs ordinateurs](#devices)
   - [Quelle option me faut-il ?](#devices-choice) ·
     [Synchroniser un profil entre PC](#sync) ·
     [Partager l'installation](#installation) ·
     [L'appareil maître](#master) ·
     [Appareil perdu ou remplacé](#remove-device)
10. [Paramètres et usage quotidien](#settings)
11. [Problèmes et réponses](#faq)
12. [Où MedReminder garde ses données](#data)
13. [Ce que MedReminder ne fait pas](#limits)

---

<a id="start"></a>
## 1. Pour commencer

<a id="first-start"></a>
### Premier démarrage

1. Lance `MedReminder.exe`.
2. La fenêtre **Bienvenue dans MedReminder** s'ouvre. Choisis :
   - **Créer le profil** — le cas normal sur ton premier ordinateur.
     Saisis ton nom et, si tu veux, un code PIN. Ce premier profil est
     l'**administrateur** : il gère l'e-mail, la sauvegarde et les
     autres profils (voir [Plusieurs personnes](#profiles)).
   - **Rejoindre une installation existante…** — seulement si
     MedReminder est déjà utilisé sur un autre de tes ordinateurs et que
     celui-ci doit en faire partie (voir
     [Partager l'installation](#installation)).
3. La fenêtre principale s'ouvre. L'icône de MedReminder dans la zone de
   notification de Windows (près de l'horloge) reste visible tant que
   l'application tourne.

**Windows SmartScreen.** Le programme est signé numériquement avec un
certificat Certum. Tant que le certificat n'a pas acquis de
réputation, au tout premier lancement Windows peut encore afficher une
fenêtre bleue « PC protégé par Windows » : clique sur **Informations
complémentaires**, puis sur **Exécuter quand même**. Windows retient ce
choix. Si tu installes depuis le paquet MSI, la fenêtre d'autorisation
affiche l'éditeur vérifié.

<a id="main-window"></a>
### La fenêtre principale

- **Menus** en haut : **Fichier**, **Traitement**, **Stock**,
  **Outils** et **?** (aide).
- **Barre d'outils** sous les menus : *Nouveau médicament*,
  *Enregistrer prise* et, à droite, une zone de recherche (**Ctrl+F**)
  qui filtre la liste par nom.
- **Navigation** à gauche : *Médicaments* (cette liste), puis
  *Chronologie du traitement*, *Fiche de traitement*, *Demander une
  ordonnance*, *Planifier le stock*, *Ordonnances*, *Échéances administratives*, *Installation* (administrateurs) et *Paramètres*, qui
  s'ouvrent dans leur propre fenêtre. Dans une fenêtre étroite, elle
  n'affiche que les icônes.
- **Résumé** au-dessus de la liste : combien de médicaments sont
  *Épuisés*, *Bientôt épuisés*, *Suspendus*, et le total. Clique sur
  une case pour n'afficher que ces médicaments ; un second clic les
  affiche tous.
- **Liste des médicaments** au centre : une ligne par médicament, avec
  le stock, les jours restants et la date estimée de fin de stock.
  Pendant la journée, le stock et les jours restants déduisent déjà
  les doses du jour dont l'heure est passée ([détails](#stock-estimate)). La
  colonne **État** montre l'état avec une étiquette colorée. Le clic
  droit sur une ligne propose les commandes pour ce médicament
  (modifier, enregistrer une prise, ajouter une boîte…) ; double-clic
  ou **F2** le modifient.
- **Barre d'état** en bas : le profil ouvert (« Profil : Anna
  (admin) » pour un administrateur).

Fermer la fenêtre avec le **X** ne quitte pas MedReminder : il continue
dans la zone de notification, et les rappels continuent d'arriver. Pour
quitter, fais un clic droit sur l'icône et choisis **Quitter**.

<a id="where"></a>
### Où trouver quoi

| Je veux… | Aller à |
|---|---|
| Ajouter un médicament | **Traitement → Nouveau médicament…** |
| Changer la dose ou la fréquence | **Traitement → Changer dose/fréquence…** |
| Enregistrer une boîte achetée | **Stock → Ajouter boîte…** ou **Stock → Réapprovisionner par code-barres…** |
| Aligner le stock sur ce que j'ai vraiment | **Stock → Compter le stock…** |
| Enregistrer une dose prise au besoin | **Traitement → Enregistrer prise…** (option *Dose supplémentaire au besoin* si le médicament a aussi des doses prévues) |
| Changer l'heure de « Le matin », « Avant le déjeuner »… | **Traitement → Heures des prises…** |
| Annuler une saisie erronée | **Stock → Historique…** |
| Imprimer le traitement pour un médecin | **Traitement → Fiche de traitement…** |
| Demander une ordonnance | **Traitement → Demander une ordonnance…** |
| Suivre une ordonnance jusqu'à la pharmacie | **Traitement → Ordonnances…** |
| Être prévenu d'un plan thérapeutique, d'une exonération ou d'un contrôle | **Traitement → Échéances administratives…** |
| Voir les prochaines dates dans Outlook, Google Agenda ou sur le téléphone | **Traitement → Exporter vers le calendrier…** |
| Vérifier le stock pour un voyage ou jusqu'au prochain passage à la pharmacie | **Traitement → Planifier le stock…** |
| Configurer e-mail, langue, sauvegarde | **Outils → Paramètres…** |
| Ajouter une personne | **Outils → Gérer les profils…** (administrateur) |
| Utiliser MedReminder sur un autre PC | **Outils → Synchronisation…** et **Outils → Installation…** (administrateur) |
| Ouvrir le profil d'une autre personne | **Fichier → Changer de profil…** |

---

<a id="medicines"></a>
## 2. Médicaments

<a id="add-medicine"></a>
### Ajouter un médicament

1. **Traitement → Nouveau médicament…** (ou le bouton *Nouveau
   médicament*).
2. Commence à saisir le **nom** : le catalogue propose les médicaments
   correspondants (voir [Trouver un médicament](#catalogue)). En choisir
   un remplit la substance active et la présentation. Tu peux aussi
   cliquer sur **Scanner le code…**.
3. Remplis les champs obligatoires, marqués d'un `*` : *Unité*, *Dose
   par prise*, *Prises par jour*, *Date de début du traitement* et
   *Seuil d'alerte (jours)*.
   - Le **seuil d'alerte** est le nombre de jours avant la fin du stock
     où tu veux être prévenu. Laisse le temps d'obtenir l'ordonnance et
     d'acheter le médicament, par exemple 10 jours.
4. **Stock initial** : combien de comprimés (ou ml, doses…) tu as
   maintenant.
5. **Canaux de notification** : coche **Notification Windows** et/ou
   **E-mail**. L'e-mail ne fonctionne qu'une fois configuré (voir
   [Notifications et e-mail](#notifications)).
6. Facultatif : *Date de fin du traitement*, *Médecin de référence*,
   *Notes*, horaires de prise, *Me rappeler à l'heure de la prise*.
7. **Enregistrer**.

À partir de là, MedReminder déduit tout seul la dose chaque jour. Tu
n'as pas besoin d'enregistrer chaque comprimé pris.

<a id="slots"></a>
### Horaires de prise

Dans **Horaires de prise (optionnel)**, tu peux répartir la dose
quotidienne : **Ajouter…** ouvre une fenêtre où tu indiques la dose,
une heure facultative (*Avec heure précise*) et une description comme
« Après le petit-déjeuner » (choisis-en une parmi les *Descriptions
courantes* ou saisis la tienne). Les horaires apparaissent sur la fiche
de traitement et, s'ils ont une heure, peuvent te rappeler la prise.

Sans horaires, le médicament utilise « dose × prises par jour ».

Une dose prise seulement en cas de besoin (par exemple un antalgique
contre le mal de tête) se marque **Au besoin** dans la fenêtre de
l'horaire : la description *Au besoin* la coche d'elle-même. Une dose au
besoin n'est jamais déduite automatiquement et ne compte pas dans le
total journalier : le stock ne diminue que lorsque tu enregistres la
prise. Si tous les horaires d'un médicament sont au besoin, il se
comporte comme un schéma *Au besoin (PRN)*.

Depuis la version qui a introduit cette option, les horaires décrits
« Au besoin » sont traités comme tels à partir de ce jour. Avant, ils
étaient déduits chaque jour : si le stock affiché est inférieur au stock
réel, fais un [comptage](#count) pour le réaligner.

**Traitement → Heures des prises…** liste les moments de la journée
avec leur heure (« Le matin » = 08:00, « Avant le déjeuner » = 13:00, …)
et les heures utilisées pour les médicaments sans horaires (1 par jour
= 08:00, 2 par jour = 08:00 et 20:00, …). Tu peux changer les heures,
masquer les moments que tu n'utilises pas et ajouter les tiens ; dans
la fenêtre de l'horaire, choisir un moment affiche son heure. Ces
heures servent seulement à placer les doses dans la journée et ne
modifient jamais le stock enregistré. Elles valent pour cet
ordinateur : elles ne sont pas synchronisées.

<a id="regimens"></a>
### Schémas complexes

Tous les traitements n'utilisent pas la même quantité chaque jour. Dans
la fenêtre du médicament, mets **Schéma** sur **Avancé** et choisis un
**Type de régime** :

| Type de régime | Exemple |
|---|---|
| **Motif hebdomadaire** | Une quantité différente pour chaque jour de la semaine, par exemple un anticoagulant à des doses différentes lun, mer, ven. |
| **Cyclique (N jours on / M off)** | Une quantité pendant N jours, puis M jours sans, par exemple 21 jours oui, 7 non. |
| **Décroissance** — *Linéaire* | La dose change d'un pas fixe tous les quelques jours jusqu'à la dose finale, puis reste constante. |
| **Décroissance** — *Par paliers* | Une liste de paliers, chacun avec sa dose et sa durée, par exemple 4 par jour pendant 7 jours, 2 par jour pendant 7 jours, 1 par jour pendant 14 jours. Utilise **Ajouter un palier** / **Supprimer** ; le total s'affiche sous la liste. Coche *Conserver la dernière dose en entretien* si la dernière dose continue indéfiniment. |
| **Au besoin (PRN)** | Aucune consommation programmée : le stock est suivi, mais aucune date de fin n'est estimée. |

Avec **Avancé**, les champs de dose en haut de la fenêtre ne sont pas
utilisés. Reviens à **Simple** pour une dose quotidienne fixe.

**Les horaires de prise fonctionnent aussi avec Avancé.** Le schéma fixe
la quantité de chaque jour ; les [horaires de prise](#slots) fixent le
moment. La quantité du jour est répartie entre les horaires en
proportion de leurs doses : avec deux horaires de 1, un jour dégressif
de 4 donne 2 + 2, un jour de 1 donne 0,5 + 0,5. Un jour de pause du
cycle, il n'y a rien à prendre et aucun rappel.

**Le traitement change ?** Utilise **Traitement → Changer
dose/fréquence…** et choisis la date **Effectif à partir de**. L'ancien
schéma reste valable pour les jours précédents. Si la date est passée,
la consommation déjà déduite depuis cette date est recalculée avec le
nouveau schéma.

MedReminder ne vérifie ni doses maximales, ni surdosages, ni
interactions médicamenteuses : il suit seulement le traitement prescrit
par ton médecin.

<a id="dose-reminder"></a>
### Rappel à l'heure de la prise

Coche **Me rappeler à l'heure de la prise** dans la fenêtre du
médicament pour recevoir un rappel (« Il est temps de prendre … ») à
chaque horaire avec heure. C'est possible quand le médicament a au moins
un horaire avec heure et qu'il reste du stock.

- Le rappel s'affiche à l'écran ; si le médicament utilise le canal
  **E-mail** et que l'e-mail est configuré, il arrive aussi par e-mail.
- **Une fois par horaire et par jour**, même si tu redémarres
  l'application.
- Si le PC était en veille à cette heure, le rappel arrive quand même
  dans les **30 minutes** ; au-delà, il est sauté.
- Avec un **stock à zéro**, aucun rappel n'est envoyé.
- La nuit du passage à l'heure d'été, un horaire situé dans l'heure
  sautée ne se déclenche pas.

Le rappel n'enregistre pas si tu as pris la dose et ne modifie pas le
stock.

<a id="edit-medicine"></a>
### Modifier, désactiver, supprimer

- **Modifier** : double-clic sur la ligne, ou **Traitement → Modifier**.
  Tu peux tout changer sauf la dose et la fréquence (utilise *Changer
  dose/fréquence…*).
- **Désactiver** : **Traitement → Désactiver** quand tu arrêtes un
  traitement. Le médicament est masqué et ne reçoit plus d'alertes ; son
  historique est conservé. **Traitement → Afficher les médicaments
  désactivés** le fait réapparaître ; pour le réactiver, ouvre-le avec
  **Modifier** et coche **Actif**. Les jours d'inactivité ne comptent
  pas comme consommation.
- **Supprimer** : **Traitement → Supprimer…** retire un médicament saisi
  par erreur. Possible seulement tant que rien n'a été enregistré (aucun
  stock, même pas la quantité initiale, aucune prise, aucun comptage).
  Sinon, désactive-le. Avec la synchronisation active, il disparaît
  aussi des autres ordinateurs.

---

<a id="catalogue"></a>
## 3. Trouver un médicament : catalogue et code-barres

### Le catalogue de référence

MedReminder contient les listes officielles des médicaments de
l'**Italie** (AIFA), de l'**Espagne** (AEMPS), de la **France** (ANSM)
et les médicaments autorisés pour toute l'**Union européenne** (EMA).

- Dans la fenêtre du médicament, saisis une partie du **nom** ou de la
  **substance active** : jusqu'à 20 résultats s'affichent. En choisir
  un remplit les autres champs.
- Un **cercle rouge** à côté d'une ligne signifie que le produit est
  suspendu ou retiré. Tu peux quand même le choisir.
- **Absent de la liste ?** Saisis simplement le nom et enregistre : le
  médicament fonctionne pareil, seulement sans lien avec le catalogue.
- **Quel pays ?** **Outils → Paramètres… → Général → Pays de
  référence** (par défaut : Italie). Seul un administrateur peut le
  changer : il s'applique à tous les profils et, avec une installation
  partagée, à tous les appareils. Avec IT, ES ou FR, la liste contient
  aussi les médicaments UE ; avec **EU**, seulement ceux-ci. Un
  médicament peut apparaître deux fois (national et UE) : choisis celui
  qui correspond à ta boîte.
- **Mises à jour automatiques.** Quand **Vérifier les mises à jour de
  l'application et des catalogues (GitHub)** est activé (Paramètres →
  Général), MedReminder télécharge au démarrage, puis une fois par jour
  tant qu'il reste ouvert, la dernière liste mensuelle de ton pays et la
  liste UE, si elles sont plus récentes. Sans connexion, rien ne change.
  Avec plusieurs profils, seul le profil ouvert est mis à jour ; les
  autres le sont la première fois qu'ils sont ouverts.

**Sources.** Données ouvertes AIFA (CC BY 4.0) ; données EMA EPAR
(avis juridique de l'EMA, décision de la Commission 2011/833/UE) ;
AEMPS CIMA (loi espagnole 37/2007 sur la réutilisation des informations
du secteur public) ; ANSM BDPM (Licence Ouverte Etalab 2.0). Les
attributions complètes sont dans **? → À propos de MedReminder…** et
dans `THIRD-PARTY-NOTICES.md`.

### Scanner le code-barres

Avec un lecteur de codes-barres USB ou une webcam, tu peux remplir un
médicament sans rien saisir.

1. Dans la fenêtre du médicament, clique sur **Scanner le code…**.
2. Scanne le code-barres de la boîte, ou saisis le code imprimé dessous
   et appuie sur **Entrée**.
3. Si le code est dans le catalogue, le formulaire est rempli. Sinon, la
   fenêtre affiche le code lu et rien ne change.

Conseils :

- Clique sur **Scanner le code…** *avant* de scanner, sinon le code est
  saisi dans le champ qui a le curseur.
- Sur les boîtes italiennes, le code à lire est le code-barres **AIC**
  (`A` suivi de 9 chiffres). Si le lecteur ne le reconnaît pas, active
  la symbologie **Code 32** (Pharmacode italien) dans ses réglages. Le
  code carré (DataMatrix) demande un lecteur 2D et n'est souvent pas
  dans le catalogue.
- Règle le lecteur sur la même disposition de clavier que Windows (par
  exemple AZERTY).

**Avec la webcam.** Clique sur **Utiliser la webcam** dans la fenêtre de
scan. Tiens la boîte à 10–20 cm, le code-barres dans le cadre, avec une
bonne lumière. La caméra s'éteint quand un code est lu, quand tu
cliques sur **Utiliser le lecteur**, quand tu fermes la fenêtre ou
après 30 secondes. Si Windows bloque la caméra, clique sur **Ouvrir les
paramètres de confidentialité**, active *Autoriser les applications de
bureau à accéder à votre caméra*, puis **Réessayer**. Aucune image n'est
enregistrée ni envoyée.

**Réapprovisionner en scannant.** **Stock → Réapprovisionner par
code-barres…** : scanne la nouvelle boîte et le médicament correspondant
s'ouvre dans la fenêtre de stock, déjà réglé sur *Nouvelle boîte* avec
la quantité habituelle. Si aucun médicament n'a ce code, tu peux
ajouter un nouveau médicament ou associer le code à un médicament
existant.

### Médicaments en pénurie (Italie)

Avec l'Italie comme pays de référence, MedReminder télécharge la liste
AIFA des médicaments en pénurie avec le catalogue (au démarrage et une
fois par jour, si **Vérifier les mises à jour de l'application et des
catalogues** est activé). Un médicament dont la boîte (code AIC, rempli
depuis le catalogue ou le code-barres) figure sur la liste l'indique
dans la colonne **Disponibilité** de la liste :

- *En pénurie* : l'AIFA indique la boîte comme difficile à trouver ;
- *Pénurie dès le …* : l'AIFA annonce une pénurie à partir de cette
  date.

Survole la cellule pour lire le début, la fin prévue (souvent non
communiquée, et elle peut changer), le motif, si l'AIFA signale des
médicaments équivalents et la date de la liste. Tu reçois aussi une
notification par pénurie, par les canaux du médicament.

Le message de pénurie n'indique aucun substitut : demande à ton médecin
ou à ton pharmacien, et demande l'ordonnance à temps. Si le
conditionnement figure aussi dans la liste de transparence de l'AIFA,
la cellule renvoie à **Médicaments équivalents (AIFA)** (voir
ci-dessous).

### Liens d'information et médicaments équivalents (Italie)

Pour un médicament avec un code de conditionnement italien (AIC), la
ligne **Informations** de la fenêtre du médicament propose jusqu'à
quatre liens :

- **Notice patient** et **Résumé des caractéristiques du produit** : les
  documents de l'AIFA, avec l'Italie comme pays de référence, quand le
  catalogue les contient ;
- **Page Codifa** : la page publique du conditionnement sur codifa.it
  (composition, classe, mode de délivrance, prix s'il est connu),
  ouverte dans le navigateur. Elle fonctionne quel que soit le pays de
  référence ;
- **Médicaments équivalents** : affiché quand le conditionnement figure
  dans la liste de transparence de l'AIFA.

Les deux dernières commandes sont aussi dans le menu contextuel de la
liste (clic droit sur un médicament) : **Médicaments équivalents
(AIFA)…** et **Ouvrir la page Codifa**. Elles sont grisées pour un
médicament sans AIC.

**Médicaments équivalents (AIFA).** Avec l'Italie comme pays de
référence, MedReminder télécharge la liste de transparence de l'AIFA
avec le catalogue (liste mensuelle, vérifiée une fois par jour). La
fenêtre montre le groupe du conditionnement (substance active, unités,
dosage, voie d'administration), le prix de référence du service de
santé et chaque conditionnement du groupe, du moins cher au plus cher,
avec son prix public, la différence payée en plus du prix de référence,
sa disponibilité, si tu l'as déjà à la maison dans un autre médicament
du profil, et la note de l'AIFA telle quelle. Une note peut limiter la
substitution (par exemple « non substituable avec … ») : lis-la. Ton
médicament est en gras.

Un conditionnement absent de la liste n'est pas « sans équivalent » :
il peut être sous brevet, en classe C ou simplement non listé. La
substitution est décidée par ton médecin et ton pharmacien ; la liste
ne tient pas compte des excipients ni des allergies. Les médicaments de
classe C n'ont pas de prix public et n'affichent aucun prix.

---

<a id="stock"></a>
## 4. Stock

MedReminder diminue le stock tout seul chaque jour selon le schéma. Tu
n'enregistres que ce qui modifie le stock autrement.

<a id="stock-estimate"></a>
Pendant la journée, la colonne du stock affiche une estimation : le
stock en début de journée moins les doses du jour dont l'heure est
passée. Les horaires sans heure prennent l'heure de leur moment
(**Traitement → Heures des prises…**) ; un horaire décrit librement,
sans heure, est compté en fin de journée. En survolant le stock, tu
vois la valeur du début de journée. Le stock enregistré, l'historique
et la date d'épuisement sont mis à jour après minuit, tandis que les
jours restants suivent le stock affiché ; si tu
enregistres une prise, ce jour compte la quantité enregistrée.

<a id="add-package"></a>
### Ajouter une boîte

1. Sélectionne le médicament.
2. **Stock → Ajouter boîte…**.
3. Choisis le type :
   - **Nouvelle boîte** — après un achat (le cas habituel) ;
   - **Ajout manuel** — par exemple des échantillons du médecin ;
   - **Correction positive** — tu avais compté trop peu.
4. Saisis la quantité et confirme.

Une nouvelle boîte relance le cycle d'alerte : quand le stock repasse
sous le seuil, tu reçois une nouvelle alerte.

Avec **Nouvelle boîte**, tu peux aussi enregistrer la péremption ; tous
les champs sont facultatifs :

- **Boîtes** — combien de boîtes identiques tu as achetées ; la quantité
  est répartie entre elles.
- **Péremption (mois/année)** — coche la case et choisis le mois et
  l'année imprimés. Une péremption `03/2027` vaut jusqu'au 31 mars 2027.
- **À utiliser dans … jours après ouverture** — pour les collyres,
  sirops, insulines en cours et autres, selon la notice ; 0 s'il n'y en
  a pas. La valeur de la dernière boîte du médicament est proposée.
- **Ouverte aujourd'hui** — si tu ouvres tout de suite la première
  boîte.
- **Lot** — facultatif.

Avec **Stock → Réapprovisionner par code-barres…**, un code DataMatrix
remplit tout seul la péremption et le lot. Si tu laisses tous ces champs
vides, la boîte ajoute seulement une quantité, comme avant.

<a id="packages"></a>
### Boîtes et péremption

**Stock → Boîtes et péremption…** (aussi depuis le menu du clic droit)
liste les boîtes du médicament sélectionné avec l'état, la péremption
imprimée, la date d'ouverture, la date limite d'utilisation et la
quantité en stock.

- Une boîte est périmée à la fin du mois imprimé, ou plus tôt quand elle
  est ouverte et que ses jours après ouverture sont écoulés (ouverte le
  1er mars, 28 jours : à utiliser jusqu'au 28 mars).
- L'app considère que la boîte ouverte est utilisée d'abord, puis celles
  qui périment en premier. Celles que le stock ne couvre plus sont
  **Épuisées** et ne donnent pas d'alerte, même si tu ne les indiques
  pas terminées. Si tu utilises les boîtes dans un autre ordre, indique
  celle en cours avec **Ouverte aujourd'hui** ou ferme la bonne.
- **Nouvelle…** enregistre une boîte que tu as déjà dans l'armoire, sans
  changer le stock.
- **Ouverte aujourd'hui**, **Terminée** : mettent à jour la boîte ; le
  stock ne change pas.
- **Jeter…** : pour une boîte jetée, en général périmée. La quantité
  restante (proposée par l'app) est retirée du stock. C'est définitif :
  en cas d'erreur, supprime la boîte et rajoute les unités avec une
  correction positive.
- **Supprimer** : seulement pour une boîte saisie par erreur ; le stock
  ne change pas.

La colonne **Péremption** de la fenêtre principale montre la première
péremption parmi les boîtes en stock, avec *(périmée)* ou *(bientôt
périmée)*. **Stock → Boîtes bientôt périmées…** réunit les boîtes
périmées ou bientôt périmées de tous les médicaments, y compris ceux qui
ne sont plus utilisés, les périmées d'abord ; **Ouvrir les boîtes…**
ouvre celles du médicament choisi. Pour les alertes, voir
[Notifications et e-mail](#notifications).

<a id="intake"></a>
### Enregistrer une prise

**Traitement → Enregistrer prise…** (ou le bouton de la barre)
enregistre une prise comme **Prise**, **Sautée** ou **Annulée**, avec le
jour et la quantité. Les jours normaux, ce n'est pas nécessaire.
Utilise-le quand un jour diffère du schéma : dès que tu enregistres une
prise pour un jour, la déduction automatique de ce jour est remplacée
par ce que tu as enregistré.

Pour une dose en plus du schéma, par exemple une dose au besoin, coche
**Dose supplémentaire au besoin** : la quantité est déduite et les doses
prévues du jour restent comptées. L'option n'apparaît que pour les
médicaments avec un schéma et elle est déjà cochée si le médicament a un
horaire au besoin.

<a id="correct"></a>
### Corriger le stock

Si tu as moins que ce qu'affiche l'application (un comprimé perdu, un
flacon renversé) : **Stock → Corriger stock…**, garde le type
**Correction négative** et saisis la quantité à retirer. Le stock ne
peut pas passer sous zéro.

<a id="count"></a>
### Compter le stock

Quand ton armoire ne correspond plus à l'application, compte et laisse
l'application corriger :

1. Sélectionne le médicament, puis **Stock → Compter le stock…**.
2. Saisis la **Quantité comptée**. La fenêtre affiche le stock attendu,
   l'écart et l'effet sur la date de fin.
3. Dans **Déjà pris aujourd'hui**, indique ce que tu avais déjà pris
   aujourd'hui au moment du comptage. L'application propose les doses
   dont l'heure est passée, celles que la liste a déjà déduites du
   stock ; les horaires sans heure prennent l'heure de leur moment.
4. Clique sur **Enregistrer le comptage**.

L'application enregistre une correction pour que le stock soit égal à
ce que tu as compté. L'écart n'est qu'un chiffre de stock : il n'est pas
interprété comme des doses oubliées ou en trop.

<a id="history"></a>
### Historique et saisies erronées

**Stock → Historique…** (Ctrl+H) liste les boîtes, corrections, prises,
comptages et suspensions du médicament sélectionné, du plus récent au
plus ancien. Sélectionne une saisie erronée et clique sur **Supprimer** :
stock et consommation sont recalculés. Seules les saisies postérieures
au dernier comptage peuvent être supprimées (pour les plus anciennes,
compte de nouveau) ; les saisies de versions antérieures à cette
fonction ne peuvent pas être supprimées, corrige-les par une correction.

---

<a id="documents"></a>
## 5. Chronologie, fiche de traitement et demande d'ordonnance

### Chronologie du traitement

**Traitement → Chronologie du traitement…** (Ctrl+T) affiche une ligne
par médicament sur un calendrier (60 jours en arrière, 120 en avant) :

- **barre pleine** : traitement en cours ; **barre hachurée** :
  suspension ;
- **losange plein** : une nouvelle dose ou un nouveau schéma commence ;
  **losange vide** : palier suivant d'une décroissance par paliers ;
- **triangle** : date estimée de fin de stock ; **ligne pointillée** :
  aujourd'hui ;
- ligne grise : médicament désactivé.

**Avant** / **Après** déplacent de 30 jours, **Aujourd'hui** revient au
présent ; les flèches du clavier déplacent d'une semaine. Le cadre des
**détails** décrit en mots le médicament sélectionné. **Afficher dans
la liste** (ou Entrée) le sélectionne dans la liste principale. La
chronologie ne modifie rien ; les dates de fin sont des estimations.

### Fiche de traitement (impression et PDF)

**Traitement → Fiche de traitement…** (Ctrl+P) prépare une fiche des
médicaments actifs pour un médecin, les urgences ou un pharmacien :
substance active, posologie, période du traitement, médecin.

- **Inclure les notes** est désactivé par défaut : les notes peuvent
  être privées.
- **Papier** : A4 ou Letter.
- **Imprimer…** affiche un aperçu ; **Enregistrer en PDF…** utilise
  l'imprimante Windows « Microsoft Print to PDF » (si elle a été
  supprimée, la fenêtre explique comment la rajouter) ; **Enregistrer
  dans un fichier…** et **Copier dans le presse-papiers** donnent le
  texte brut.

MedReminder ne garde aucune copie de ce que tu enregistres ou imprimes.

### Planifier le stock (voyage ou pharmacie)

**Traitement → Planifier le stock…** répond à la question « en ai-je
assez jusqu'au… ? ». Choisis la période avec **Du** et **Au**, par
exemple les jours d'un voyage ou les jours jusqu'à ton prochain passage
à la pharmacie (par défaut : les 14 prochains jours, aujourd'hui
compris). Pour chaque médicament actif, la fenêtre affiche :

- **Besoin sur la période** : la quantité consommée sur la période,
  selon le schéma, les suspensions, la date de fin du traitement et les
  horaires de prise ;
- **Stock au début** : le stock d'aujourd'hui moins la consommation
  prévue jusqu'au début de la période (*épuisé avant* s'il n'en restera
  pas) ;
- **Manque** : ce qu'il faut au-delà de ce stock, ou *couvert* ;
- **Boîtes à obtenir** : combien de boîtes couvrent ce qui manque, de la
  taille de la dernière nouvelle boîte enregistrée (— si aucune n'a été
  enregistrée).

Les médicaments non couverts apparaissent en premier. Les médicaments
« si besoin » sont listés mais pas calculés, car leur consommation n'est
pas planifiée. **Imprimer…**, **Enregistrer en PDF…** et **Copier dans
le presse-papiers** fonctionnent comme pour la fiche de traitement. La
fenêtre ne modifie rien : les chiffres sont des estimations.

### Demander une ordonnance

Sélectionne un médicament, puis **Traitement → Demander une
ordonnance…**. MedReminder prépare un court message avec le nom du
médicament, la présentation, le code du produit et ton nom ; avec un
médecin de référence, la formule de politesse utilise son nom. Ni
posologie ni notes ne sont incluses. Tu peux tout modifier avant
l'envoi.

- **Copier** — à coller dans un webmail, une messagerie ou un portail
  patient.
- **Ouvrir dans la messagerie** — un nouvel e-mail dans ton logiciel de
  messagerie habituel.
- **Envoyer…** — l'envoie avec le compte e-mail de MedReminder, après
  confirmation. Disponible quand l'e-mail est configuré et que l'**E-mail
  du médecin** est renseigné (Paramètres → Notifications). Avec une
  installation partagée, seul l'[appareil maître](#master) envoie : sur
  les autres appareils, utilise **Ouvrir dans la messagerie**.

MedReminder n'envoie jamais de demande tout seul.

### Suivre une ordonnance jusqu'à la pharmacie

**Traitement → Ordonnances…** liste les ordonnances enregistrées,
d'abord celles à retirer. Pour chacune tu peux noter, quand tu les
connais :

- **Demandée le** : quand tu l'as demandée au médecin. **Marquer comme
  demandée** dans la fenêtre de demande l'enregistre pour toi à la date
  du jour ;
- **Émise le**, **Code de l'ordonnance** et **Boîtes** : d'après
  l'ordonnance émise par le médecin ;
- **Valable jusqu'au** : le dernier jour où la pharmacie l'accepte. Il
  est rempli pour 30 jours à partir de la date d'émission, la validité
  habituelle de l'ordonnance électronique italienne ; vérifie-le sur ton
  ordonnance et corrige-le s'il est différent ;
- **Retirée le** : quand tu l'as présentée à la pharmacie. **Retirée
  aujourd'hui** le fait en un clic. Quand tu ajoutes une nouvelle boîte
  d'un médicament dont une ordonnance est encore à retirer, MedReminder
  demande si la boîte en vient.

Une ordonnance émise et non retirée est *À retirer* ; après son dernier
jour de validité elle est *Expirée*. À partir de 3 jours avant ce jour,
tu reçois un rappel, une fois, par les canaux de notification du
médicament (l'e-mail seulement depuis l'[appareil maître](#master)
si l'installation est partagée). Le rappel ne contient pas le code.

Les ordonnances sont copiées sur les autres PC d'un profil synchronisé
et incluses dans l'export chiffré.

### Ordonnances renouvelables

Certaines ordonnances couvrent plusieurs délivrances en pharmacie sur
une longue validité, par exemple une année de traitement retirée un
mois à la fois. Coche **Ordonnance renouvelable** dans la fenêtre de
l'ordonnance pour l'enregistrer comme une seule ordonnance :

- **Délivrances prévues** : combien de fois la pharmacie la délivre (de
  2 à 12) ;
- **Valable jusqu'au** est rempli pour 12 mois à partir de la date
  d'émission ; vérifie-le sur ton ordonnance et corrige-le s'il est
  différent ;
- **Délivrances retirées** remplace *Retirée le* : ajoute chaque
  délivrance avec le jour et, si tu le connais, le nombre de boîtes.
  **Retirée aujourd'hui** dans la liste en enregistre une en un clic, et
  après une nouvelle boîte MedReminder propose de l'enregistrer à la
  date du jour.

La liste affiche les délivrances sous la forme retirées / prévues, par
exemple `3 / 12`. Une ordonnance renouvelable reste *À retirer* tant
qu'il reste des délivrances et que la validité dure ; elle est
*Retirée* quand toutes ont été retirées et *Expirée* si la validité se
termine avant. Quand le stock baisse, l'alerte indique combien de
délivrances restent et jusqu'à quand, au lieu de suggérer une nouvelle
ordonnance, et son bouton ouvre l'ordonnance. Le rappel avant *Valable
jusqu'au* n'arrive que s'il reste des délivrances et indique combien
seraient perdues. MedReminder ne contrôle pas l'intervalle entre les
délivrances : suis les indications de ton pharmacien.

Sur des PC synchronisés, mets à jour MedReminder sur tous les PC du
profil avant d'enregistrer une ordonnance renouvelable : une version
plus ancienne arrête la synchronisation jusqu'à sa mise à jour.

### Service régional des ordonnances

Avec l'Italie comme pays de référence, **Traitement → Ordonnances…** et
la fenêtre de demande d'ordonnance ont un bouton **Service régional des
ordonnances**. Il ouvre le service de votre région où sont affichées
les ordonnances électroniques émises pour vous, pour copier le numéro
de l'ordonnance au lieu de l'attendre.

- La première fois, choisissez votre région ou province autonome. Elle
  est enregistrée avec le profil ; changez-la depuis le bouton
  (**Changer de région…**) ou dans Paramètres → Notifications.
- **Ouvrir dans le navigateur** ouvre le portail régional dans votre
  navigateur habituel.
- **Ouvrir sur le téléphone** affiche un code QR de l'application
  régionale, ou du portail s'il n'y a pas d'application : scannez-le
  avec l'appareil photo du téléphone et connectez-vous sur le
  téléphone.

La ligne sous le bouton indique le service et comment se connecter
(SPID, CIE ou TS-CNS). Vous vous connectez sur le service régional,
jamais dans MedReminder : MedReminder ne voit ni vos identifiants ni
votre dossier de santé et n'en importe rien. Un aidant se connecte avec
ses propres identifiants et une délégation configurée sur le service
régional. Si aucun service n'est répertorié pour votre région, ouvrez
vous-même le portail du dossier de santé (Fascicolo Sanitario
Elettronico) de votre région.

**Coller le NRE**, à côté de **Code de l'ordonnance** dans la fenêtre
de l'ordonnance, écrit le numéro d'ordonnance électronique (NRE, 15
lettres ou chiffres) copié depuis le service régional, sans espaces, et
remplit **Émise le** avec la date du jour s'il est vide. MedReminder ne
lit le presse-papiers que lorsque vous cliquez.

### Échéances administratives

**Traitement → Échéances administratives…** regroupe les dates qui ne
concernent pas le stock : le renouvellement d'un plan thérapeutique ou
d'une exonération, un contrôle périodique, ou toute autre chose que
vous décrivez. Pour chaque échéance :

- **Type** et **Description** : la description est facultative, sauf
  pour le type *Autre* ;
- **Médicament** : le médicament concerné, ou *(aucun)* pour une
  échéance de tout le profil ;
- **Date** et **Prévenir jours avant** : le rappel commence ce nombre
  de jours avant la date (14 par défaut) ;
- **Répéter tous les … mois** : pour une échéance qui revient, comme un
  renouvellement annuel ;
- **Prévenir par** : notification Windows et/ou e-mail.

MedReminder n'applique aucune règle propre à ces dates : les durées de
validité varient selon le plan et la région, saisissez donc la date
indiquée sur vos documents.

À partir du préavis, vous recevez un rappel par date, sur les canaux
choisis (l'e-mail uniquement depuis l'[appareil maître](#master) quand
l'installation est partagée) ; une échéance dépassée s'affiche en
rouge. **Faite** clôt une échéance unique ; une échéance récurrente
passe à sa date suivante, comptée depuis la date précédente et non
depuis le jour où vous l'avez marquée.

Les échéances sont copiées sur les autres PC d'un profil synchronisé et
incluses dans l'export chiffré.

### Exporter les dates vers un agenda

**Traitement → Exporter vers le calendrier…** enregistre un fichier
`.ics` avec les prochaines dates : pour chaque médicament actif le jour
où demander l'ordonnance (la date d'épuisement moins le seuil d'alerte)
et la date d'épuisement, le dernier jour pour retirer chaque
ordonnance et les échéances administratives ouvertes. Ouvrez le
fichier avec Outlook, Google Agenda ou l'agenda de votre téléphone. Les
événements sont des rappels, pas des rendez-vous : ils ne vous
marquent pas comme occupé. Un nouvel export met à jour les mêmes
événements au lieu d'ajouter des copies.

Les agendas sont souvent stockés en ligne par une autre entreprise :
les événements indiquent donc seulement quoi faire (« MedReminder : un
médicament s'épuise »). Cochez **Inclure les noms des médicaments et
les descriptions des échéances** si vous voulez les noms dans
l'agenda ; le choix est demandé à chaque export.

Chaque e-mail de stock faible contient aussi la date d'épuisement sous
forme de fichier d'agenda (`medreminder.ics`), avec le même titre
générique.

---

<a id="notifications"></a>
## 6. Notifications et e-mail

### Comment fonctionnent les alertes

- Toutes les 30 minutes, MedReminder vérifie les médicaments. Quand un
  médicament passe sous son **seuil d'alerte**, il te prévient **une
  fois**, par les canaux choisis pour ce médicament : une notification
  Windows et/ou un e-mail.
  La vérification utilise le stock enregistré, pas l'estimation de la
  liste : le jour où le seuil est franchi, la liste peut afficher
  l'état d'alerte quelques heures avant l'avertissement.
- Si aucune nouvelle boîte n'a été ajoutée quand les jours restants
  atteignent **la moitié du seuil**, un **deuxième rappel** suit par les
  mêmes canaux (avec un seuil de 10 jours : première alerte à 10 jours,
  deuxième à 5). Un médicament déjà sous la moitié lors de la première
  vérification reçoit seulement le deuxième rappel. Après une nouvelle
  boîte, le cycle recommence.
- **Péremption des boîtes** : une boîte enregistrée avec une péremption
  donne une alerte *bientôt périmée* 30 jours avant la péremption
  imprimée (3 jours avant la fin de la période après ouverture) et une
  alerte *périmée* le lendemain, une fois chacune, sur les canaux du
  médicament, même s'il n'est plus utilisé. Les boîtes épuisées ou
  fermées ne donnent pas d'alerte. Les délais se changent dans
  **Outils → Paramètres… → Notifications → Péremption des boîtes** ;
  avec 0, seule l'alerte de boîte périmée reste.
- **Depuis la notification Windows** : un clic ouvre MedReminder sur ce
  médicament (sur les ordonnances, pour un rappel d'ordonnance ; sur les échéances, pour un rappel d'échéance ; sur les boîtes, pour une alerte de péremption). Une
  alerte de stock a **Préparer la demande**, qui ouvre la demande au
  médecin ; un rappel de dose a **Me le rappeler dans 15 minutes**, qui
  le reprend plus tard, même si MedReminder est fermé entre-temps. Les
  prises ne s'enregistrent pas depuis la notification : utilise
  **Traitement → Enregistrer prise…**.
- **Outils → Vérifier maintenant** (**Ctrl+R**, ou le menu de l'icône)
  lance la vérification tout de suite.
- MedReminder doit tourner pour envoyer les alertes. Active le
  démarrage automatique (voir [Paramètres](#settings)).

### Étape 1 — le compte e-mail (administrateur)

**Outils → Paramètres… → E-mail SMTP** :

| Champ | Quoi saisir |
|---|---|
| **Hôte** | Le serveur d'envoi de ton fournisseur, par exemple `smtp.gmail.com` |
| **Port** | `587` avec *Utiliser StartTLS* coché, ou `465` avec *Utiliser StartTLS* décoché |
| **Nom d'utilisateur** / **Nouveau mot de passe** | Ton compte e-mail. Le mot de passe est enregistré chiffré et n'apparaît jamais dans les journaux |
| **Expéditeur (from)** / **Nom d'expéditeur** | De qui viennent les e-mails |
| **Délai (s)** | Secondes avant d'abandonner |

Clique sur **Tester la connexion** (il se connecte sans rien envoyer),
puis sur **Enregistrer les paramètres SMTP**.

Les valeurs pour Gmail et les autres fournisseurs courants, et la façon
d'obtenir un mot de passe d'application, sont dans
[Paramètres e-mail des principaux fournisseurs](#smtp-providers).

### Étape 2 — les destinataires (chaque profil)

**Outils → Paramètres… → Notifications**, pour le profil ouvert :

- **Destinataire (to)** — qui reçoit les alertes de ce profil.
- **E-mail de l'aidant (facultatif)** — un proche ou un aidant qui
  reçoit une copie des alertes, dans le même e-mail (les deux
  adresses sont visibles des deux). Elle doit différer du destinataire.
  Sous **Copie à l'aidant**, choisissez les alertes qu'il reçoit
  (toutes tant que vous ne changez rien) : stock faible, rappels de
  dose, d'ordonnance et d'échéance, avis de pénurie, avis de péremption
  des boîtes. **Envoyer à l'aidant un résumé hebdomadaire du stock**
  ajoute, tous les 7 jours, un e-mail à l'aidant seul avec le stock,
  l'état et la date d'épuisement de chaque médicament actif et les
  boîtes périmées ou bientôt périmées, et rien sur les doses prises. Il est envoyé par le PC qui envoie les e-mails, une fois par
  profil même si le profil est synchronisé sur plusieurs PC.
- **E-mail du médecin (facultatif)** — utilisé seulement pour les
  demandes d'ordonnance que tu envoies toi-même ; les alertes
  automatiques n'y vont jamais.
- **Péremption des boîtes** — combien de jours avant arrive l'alerte
  *bientôt périmée* : avant la péremption imprimée (30 par défaut) et
  avant la fin de la période après ouverture (3 par défaut).

Clique sur **Enregistrer les destinataires**. Dans la même section,
**Mon code PIN** permet de définir ou changer le PIN de ton propre
profil.

<a id="smtp-providers"></a>
### Paramètres e-mail des principaux fournisseurs

Beaucoup de fournisseurs n'acceptent plus, dans les programmes, le mot
de passe de ton webmail. Ils demandent un **mot de passe
d'application** : un mot de passe distinct, créé par le fournisseur pour
un seul programme et révocable à tout moment. Saisis-le dans **Nouveau
mot de passe**. Si tu le révoques, MedReminder n'envoie plus rien tant
que tu n'en saisis pas un nouveau.

Une seule règle pour le port et le chiffrement :

| Port | *Utiliser StartTLS* |
|---|---|
| `587` | coché |
| `465` | décoché (la connexion est chiffrée dès le début) |

Avec tous les fournisseurs ci-dessous, le **Nom d'utilisateur** est ton
adresse e-mail complète. Utilise la même adresse comme **Expéditeur
(from)** : beaucoup de fournisseurs refusent un expéditeur différent du
compte.

| Fournisseur | Hôte | Port | Mot de passe |
|---|---|---|---|
| Gmail | `smtp.gmail.com` | `587` | Mot de passe d'application (voir plus bas) |
| Yahoo Mail | `smtp.mail.yahoo.com` | `465` | Mot de passe d'application, depuis la page *Sécurité* du compte Yahoo |
| iCloud Mail | `smtp.mail.me.com` | `587` | Mot de passe pour app, depuis `account.apple.com` → *Connexion et sécurité* ; exige l'identification à deux facteurs |
| Libero Mail | `smtp.libero.it` | `465` | Mot de passe du compte ; avec la validation en deux étapes, un mot de passe d'application depuis *Gestione Account* |
| Aruba (y compris boîtes de domaine) | `smtps.aruba.it` | `465` | Mot de passe de la boîte |
| GMX | `mail.gmx.net` | `587` | Mot de passe du compte ; active d'abord *POP3/IMAP* dans les paramètres e-mail du webmail |
| WEB.DE | `smtp.web.de` | `587` | Mot de passe du compte ; active d'abord *POP3/IMAP* dans les paramètres e-mail du webmail |
| Orange | `smtp.orange.fr` | `465` | Mot de passe du compte ; s'il est refusé, vérifie dans ton espace client Orange si un mot de passe dédié est nécessaire |

**Outlook.com, Hotmail, Live, MSN.** Microsoft n'accepte pour ces
comptes que la connexion moderne (OAuth2), que MedReminder ne prend pas
en charge ; un mot de passe d'application ne fonctionne pas non plus. Il
en va en général de même pour les comptes professionnels ou scolaires
Microsoft 365. Utilise un autre compte pour l'envoi, par exemple une
adresse Gmail réservée à MedReminder.

#### Gmail : créer le mot de passe d'application

1. Connecte-toi sur `myaccount.google.com` avec le compte Gmail qui
   enverra les e-mails.
2. Ouvre **Sécurité**. Si la **Validation en deux étapes** n'est pas
   activée, active-la avec la procédure guidée (téléphone ou application
   d'authentification). Sans elle, les mots de passe d'application
   n'existent pas.
3. Ouvre `myaccount.google.com/apppasswords`, ou cherche « Mots de passe
   des applications » dans la zone de recherche du compte. Google peut
   te redemander ton mot de passe.
4. Saisis un nom qui rappelle son usage, par exemple `MedReminder`, et
   clique sur **Créer**.
5. Google affiche un mot de passe de 16 lettres en quatre groupes.
   Copie-le et colle-le dans **Nouveau mot de passe**, sans espaces.
   Google ne l'affiche plus : si tu le perds, supprime-le sur la même
   page et crées-en un autre.
6. Dans MedReminder, saisis l'hôte `smtp.gmail.com`, le port `587`,
   *Utiliser StartTLS* coché, et ton adresse Gmail comme **Nom
   d'utilisateur** et comme **Expéditeur (from)**. Clique sur **Tester
   la connexion**, puis sur **Enregistrer les paramètres SMTP**.

Si la page indique que l'option n'est pas disponible, en général la
validation en deux étapes n'est pas activée ou n'utilise que des clés de
sécurité, le compte est inscrit au Programme Protection Avancée, ou
c'est un compte professionnel ou scolaire dont l'administrateur a
désactivé les mots de passe d'application. Si tu changes le mot de passe
de ton compte Google, Google révoque les mots de passe d'application :
crées-en un nouveau et saisis-le dans MedReminder.

Les fournisseurs changent leurs règles et leurs adresses. Si **Tester la
connexion** échoue avec les valeurs ci-dessus, consulte la page d'aide
de ton fournisseur (cherche « paramètres SMTP »).

---

<a id="profiles"></a>
## 7. Plusieurs personnes : profils et rôles

Un seul MedReminder peut suivre les médicaments de plusieurs personnes,
par exemple toi et un parent. Chaque personne a un **profil** avec ses
médicaments et ses destinataires.

### Rôles

| | Administrateur | Utilisateur |
|---|---|---|
| Ses médicaments, stock, destinataires | oui | oui |
| Compte e-mail, sauvegarde, pays de référence | oui | non |
| Créer, renommer, supprimer des profils ; PIN de tous | oui | non |
| Outils → Synchronisation… et Outils → Installation… | oui | non |

Il y a toujours au moins un administrateur.

### Gérer les profils (administrateur)

**Outils → Gérer les profils…** :

- **Nouveau profil** — nom, rôle (par défaut *Utilisateur*), PIN
  facultatif.
- **Renommer** — change le nom affiché.
- **Changer le PIN** — définit, change ou efface le PIN d'un profil.
- **Changer le rôle…** — rend un profil administrateur ou utilisateur.
  Le rôle du profil ouvert ne peut pas être changé (ouvre d'abord un
  autre profil administrateur). Avant de rendre administrateur un profil
  sans PIN, pense à lui ajouter un PIN.
- **Supprimer** — saisis le nom du profil pour confirmer. Les données
  sur le disque sont conservées sauf si tu coches *Supprimer aussi les
  données du profil sur le disque*. Le profil ouvert et le dernier
  administrateur ne peuvent pas être supprimés.

### Changer de profil

**Fichier → Changer de profil…**, choisis le profil, confirme.
MedReminder redémarre avec ce profil (et demande son PIN, s'il en a
un). Au démarrage de Windows, le dernier profil utilisé s'ouvre.

### À propos du PIN

Le PIN évite d'ouvrir par erreur le mauvais profil. Ce n'est **pas** une
protection : il ne chiffre rien, et toute personne utilisant le même
compte Windows peut lire les fichiers de tous les profils. Trois essais
erronés ferment l'application. Pour une vraie confidentialité, donne à
chacun son propre compte Windows. Si un PIN est oublié, un
administrateur l'efface avec **Changer le PIN** ; si le seul
administrateur l'a oublié, voir [Problèmes et réponses](#faq).

---

<a id="backup"></a>
## 8. Protéger tes données : sauvegarde et export

| Option | À quoi elle sert | Où |
|---|---|---|
| **Sauvegarde automatique quotidienne** | Une copie de tous les profils, chaque jour, dans un dossier à toi | Paramètres → Sauvegarde / Restauration |
| **Export chiffré** | Un seul fichier portable, pour passer à un nouveau PC ou le garder en lieu sûr | Paramètres → Sauvegarde / Restauration → Exporter toutes les données (chiffrées)… |
| **Sauvegarde cloud** | Une copie chiffrée quotidienne dans OneDrive, Google Drive ou un dossier synchronisé | Paramètres → Sauvegarde / Restauration → Sauvegarde vers un dossier synchronisé |
| **Synchronisation / Installation** | Plusieurs PC qui travaillent en continu sur les mêmes données | [Plusieurs ordinateurs](#devices) |

Les paramètres de sauvegarde sont gérés par un administrateur.

### Sauvegarde automatique quotidienne

1. **Outils → Paramètres… → Sauvegarde / Restauration**.
2. Coche **Sauvegarde automatique quotidienne**, choisis le **Dossier de
   sauvegarde** (idéalement un disque externe), l'**Heure préférée** et
   la **Rétention (jours)**.
3. **Enregistrer les paramètres de sauvegarde**. **Sauvegarder
   maintenant** en fait une tout de suite.

Chaque profil est sauvegardé, sous la forme
`medreminder-<profil>-<date>-<heure>.db`. MedReminder doit tourner à
l'heure choisie ; si le PC était éteint, la sauvegarde se fait au
démarrage suivant. Ne choisis pas un dossier synchronisé dans le cloud
pour cette sauvegarde : les fichiers ne sont pas chiffrés (MedReminder
te prévient).

- **Exporter dans un dossier spécifique…** — une copie tout de suite, où
  tu veux.
- **Restaurer une sauvegarde…** — choisis un fichier `.db` et le profil
  qui le reçoit. Les données actuelles sont mises de côté sous
  `medreminder.db.bak-<date>`. Si tu restaures dans le profil ouvert,
  MedReminder redémarre.

### Export et importation chiffrés

Un export est **un seul fichier chiffré** (`.mrz`) avec toutes les
données d'un profil. Il n'est pas lié à ton PC : c'est la façon
recommandée de passer à un nouvel ordinateur.

**Exporter** — **Paramètres → Sauvegarde / Restauration → Exporter
toutes les données (chiffrées)…** :

1. Choisis le fichier de destination.
2. Choisis une **phrase de passe** d'au moins 12 caractères et saisis-la
   deux fois.
3. Si tu veux, inclus le mot de passe SMTP, les préférences de
   sauvegarde et les préférences utilisateur (langue, pays du
   catalogue). Le mot de passe SMTP est chiffré avec ta phrase de passe.
4. **Exporter**.

Un administrateur avec plusieurs profils peut cocher **Exporter tous les
profils (un fichier chiffré par profil)** et choisir un dossier.

> **La phrase de passe ne peut pas être récupérée.** Sans elle, le
> fichier ne pourra plus jamais être lu. Note-la en lieu sûr.

**Importer** — **Paramètres → Sauvegarde / Restauration → Importer
depuis un export…** : choisis le fichier (MedReminder montre ce qu'il
contient), saisis la phrase de passe, coche *Je comprends que cette
action écrasera les données du profil actuel*, clique sur **Importer**
et redémarre quand c'est demandé. L'importation **remplace** les données
du profil ouvert ; une copie de sécurité est conservée. Une phrase de
passe erronée, un fichier endommagé ou un fichier d'une version plus
récente arrêtent l'importation sans toucher à tes données. Le format
est public (`docs/EXPORT-FORMAT.md`) : tes données ne sont jamais
enfermées.

### Sauvegarde cloud

Une copie chiffrée de tous les profils, une fois par jour, dans le
cloud.

1. **Paramètres → Sauvegarde / Restauration → Sauvegarde vers un
   dossier synchronisé (chiffrée)** : coche la case.
2. **Stockage** :
   - **OneDrive (dossier d'application)** ou **Google Drive (dossier
     MedReminder/backups)** — clique sur **Se connecter…** et connecte-toi
     avec ton compte ;
   - **Dossier** — un dossier déjà synchronisé par OneDrive, Dropbox,
     iCloud ou Google Drive sur ce PC.
3. **Instantanés à conserver** (30 par défaut).
4. **Phrase de passe de sauvegarde → Définir / changer…** : au moins 12
   caractères. Elle reste sur ce PC et n'est jamais envoyée.
5. Enregistre.

**Restaurer** — **Paramètres → Sauvegarde / Restauration → Restaurer
depuis le dossier cloud…** : choisis le dossier ou le compte, choisis
une copie (date, profil, appareil), saisis la phrase de passe, coche la
confirmation et clique sur **Restaurer**. La copie remplace le profil
**ouvert** : pour restaurer un autre profil, ouvre-le d'abord.

Bon à savoir :

- Perdre la phrase de passe de sauvegarde, c'est perdre les copies.
- Les copies ne contiennent ni le mot de passe SMTP ni les préférences :
  utilise l'export chiffré pour cela.
- Qui connaît la phrase de passe peut lire la copie de chaque profil, y
  compris ceux protégés par un PIN.
- Ton fournisseur cloud peut garder les fichiers supprimés dans sa
  corbeille.
- C'est une **sauvegarde, pas une synchronisation** : pour travailler
  sur plusieurs PC, utilise la [synchronisation](#sync).
- Avec une installation partagée, seul l'[appareil maître](#master) fait
  la sauvegarde cloud.

---

<a id="devices"></a>
## 9. Plusieurs ordinateurs

<a id="devices-choice"></a>
### Quelle option me faut-il ?

| Situation | Utiliser |
|---|---|
| Un seul PC | Rien à faire. Garde une [sauvegarde](#backup). |
| Passer une fois à un nouveau PC | [Export chiffré](#backup) sur l'ancien PC, importation sur le nouveau. |
| Le **même profil** sur deux PC ou plus, toujours à jour | [Synchronisation](#sync) (Outils → Synchronisation…). |
| **Toute la configuration familiale** (profils, rôles, PIN, e-mail, sauvegarde) sur plusieurs PC, avec **un seul** PC qui envoie les e-mails | [Installation](#installation) (Outils → Installation…), en plus de la synchronisation. |

Les deux options demandent un stockage accessible à tous les PC :
**OneDrive**, **Google Drive** ou un **dossier partagé** (un dossier
synchronisé par Dropbox ou équivalent, ou un partage réseau). Les
données y sont toujours chiffrées. Aucun serveur MedReminder n'est
utilisé.

Tous les PC d'un groupe doivent avoir la même version de MedReminder :
mets-les à jour ensemble.

<a id="sync"></a>
### Synchroniser un profil entre PC

La synchronisation garde **un profil** identique sur plusieurs PC :
médicaments, stock, prises, nom du profil et destinataires. Ce que tu
enregistres sur un PC apparaît sur les autres en quelques minutes. Elle
se configure **pour chaque profil**, profil ouvert, par un
administrateur.

**Sur le premier PC**

1. Ouvre le profil, puis **Outils → Synchronisation… → Activer la
   synchronisation…**.
2. Choisis où vit le groupe :
   - **OneDrive** ou **Google Drive** : connecte-toi dans la fenêtre du
     navigateur. Tous les PC doivent utiliser le **même** compte.
     MedReminder n'utilise que son propre dossier d'application ;
   - **un dossier partagé** : choisis-le.
3. Saisis un nom pour ce PC et une **phrase secrète de
   synchronisation** (au moins 10 caractères, deux fois). Ce n'est pas
   la phrase de passe de sauvegarde. Garde-la bien : elle ne peut pas
   être récupérée.

**Sur chaque autre PC**

1. Crée un profil (n'importe quel nom : il sera remplacé), ou ouvre
   celui à remplacer.
2. **Outils → Synchronisation… → Rejoindre un groupe…**, choisis le même
   stockage, saisis un nom pour ce PC et la même phrase secrète. Au
   lieu de la phrase secrète, tu peux utiliser **Rejoindre avec un code
   d'association…** (voir plus bas).
3. Confirme : **les données de ce profil sur ce PC sont remplacées** par
   celles du groupe (une copie est conservée). MedReminder redémarre.

Avec un dossier partagé, attends d'abord qu'il soit entièrement
téléchargé sur le nouveau PC. Avec OneDrive ou Google Drive, l'adhésion
peut prendre une minute. Si la phrase secrète ouvre plusieurs groupes
(plusieurs profils synchronisés avec la même phrase secrète),
MedReminder demande lequel rejoindre.

**Code d'association au lieu de la phrase secrète.** Sur un PC déjà dans
le groupe, **Outils → Synchronisation… → Associer un appareil…** et
clique sur **Afficher le code** quand l'autre PC est prêt. Sur l'autre
PC, **Rejoindre avec un code d'association…** et saisis le code. Le code
est valable 10 minutes et seulement tant que sa fenêtre est ouverte.
Quiconque le voit peut lire les données : ne l'envoie jamais par e-mail
ou message, et affiche-le seulement quand il le faut (les outils
d'assistance à distance le voient aussi).

**Au quotidien**

- La synchronisation a lieu quelques secondes après chaque modification,
  toutes les 5 minutes et avec **Synchroniser maintenant**. La section
  **Appareils** montre les PC et quand chacun a été vu pour la dernière
  fois.
- Si deux PC ont changé la même chose avant de se synchroniser, la
  modification la plus récente l'emporte et le cas apparaît dans
  **Conflits** : **Restaurer la valeur perdue** rétablit l'autre valeur,
  **Ignorer** retire l'entrée.
- Un e-mail de stock bas est envoyé **une fois par groupe**, pas une fois
  par PC. (Deux PC qui vérifient avant de s'être synchronisés peuvent
  l'envoyer tous les deux ; un [appareil maître](#master) supprime ce
  cas.)
- Importer un export ou restaurer une sauvegarde sur un profil
  synchronisé démarre une nouvelle **génération** : les autres PC sont
  prévenus et doivent utiliser **Reconstruire depuis le groupe…**.
- **Désactiver la synchronisation…** arrête la synchronisation sur ce PC
  et conserve ses données.
- Si la session OneDrive ou Google Drive expire (changement de mot de
  passe, longue inactivité), clique sur **Se reconnecter à OneDrive** /
  **Se reconnecter à Google Drive** ; rien n'est perdu.

**Un PC est perdu ou la phrase secrète a fuité.** Dans la section
**Appareils**, sélectionne le PC et clique sur **Retirer l'appareil…**,
ou utilise **Changer la clé et la phrase secrète…**. Choisis une
nouvelle phrase secrète de synchronisation : le PC retiré ne pourra rien
lire de ce qui sera écrit ensuite. Déconnecte aussi ce PC dans les
paramètres de sécurité de ton compte Microsoft ou Google. Sur chaque
autre PC, clique sur **Saisir la nouvelle clé…** et saisis la nouvelle
phrase secrète ou un code d'association : ses modifications sont
conservées, et MedReminder redémarre.

<a id="installation"></a>
### Partager l'installation

La synchronisation fonctionne profil par profil. L'**installation**
ajoute tout ce qui entoure les profils, pour que chaque PC soit
configuré de la même façon :

| Partagé par tous les appareils | Propre à chaque appareil |
|---|---|
| Profils : noms, rôles, PIN | Les profils que l'appareil contient |
| Compte e-mail (SMTP, mot de passe compris) | Langue de l'interface, taille du texte |
| Règles de la sauvegarde cloud (stockage, nombre de copies) | Phrase de passe et connexion de la sauvegarde cloud |
| Pays de référence | Sauvegarde automatique locale |

Chaque appareil ne contient **que les profils qu'un administrateur lui
attribue** : le PC d'un grand-parent peut ne contenir que son profil,
alors que le PC familial les contient tous.

**Avant de commencer**

- Un profil administrateur, sur le PC qui sera le principal.
- **La synchronisation activée pour chaque profil** à partager (voir
  [Synchronisation](#sync)) ; un profil sans synchronisation ne peut
  pas être attribué à un autre appareil.

**Étape 1 — Publier (sur le PC principal)**

1. **Outils → Installation… → Publier l'installation…**.
2. Choisis le **même stockage** que celui des groupes de
   synchronisation des profils.
3. Choisis une **phrase secrète de l'installation**. Elle permet à un
   administrateur d'ajouter un appareil et de récupérer tous les profils
   quand aucun autre appareil n'est à portée de main. Réserve-la aux
   administrateurs ; elle ne peut pas être récupérée.

Ce PC devient l'[appareil maître](#master).

**Étape 2 — Ajouter un appareil**

1. Sur le PC principal : **Outils → Installation… → Appareils → Ajouter
   un appareil…**, coche les profils pour le nouvel appareil, puis
   **Afficher le code**.
2. Sur le nouveau PC :
   - si MedReminder n'y a jamais été utilisé : dans la fenêtre de
     bienvenue, choisis **Rejoindre une installation existante…** ;
   - sinon : **Outils → Installation… → Rejoindre une installation
     existante…**.
3. Choisis **Avec un code** et saisis le code. Le code dure 10 minutes,
   ou jusqu'à la fermeture de sa fenêtre.
4. La fenêtre liste chaque profil (« ajouté à cet appareil », « déjà sur
   cet appareil », …). MedReminder redémarre avec les nouveaux profils et
   les paramètres de l'installation. Les profils déjà présents sur le
   nouveau PC sont ajoutés à l'installation.

*Aucun autre appareil à portée de main ?* Choisis **Avec la phrase
secrète de l'installation**, sélectionne le stockage et saisis la phrase
secrète. Un administrateur choisit ensuite son profil, saisit son PIN et
sélectionne les profils pour cet appareil.

**Au quotidien**

- L'installation se synchronise toute seule toutes les 15 minutes.
- Un changement de profil, rôle, PIN, compte e-mail, règles de
  sauvegarde cloud ou pays de référence fait sur un appareil atteint les
  autres.
- Un profil créé plus tard : active sa synchronisation (Outils →
  Synchronisation…), puis attribue-le à d'autres appareils avec
  **Ajouter un appareil…** depuis un appareil qui le contient.
- **Outils → Installation… → État** montre le stockage, le maître et
  les actions éventuelles à faire.

<a id="master"></a>
### L'appareil maître

Dans une installation partagée, **un seul appareil, le maître**, envoie
tous les e-mails (alertes de stock bas et rappels de prise, de tous les
profils qu'il contient, même ceux qui ne sont pas ouverts) et fait la
sauvegarde cloud. Les autres appareils n'affichent leurs alertes qu'à
l'écran. Ainsi chaque e-mail arrive une seule fois.

- L'appareil qui publie l'installation est le maître. La section
  **Appareils** l'indique dans la colonne **Rôle**.
- Choisis comme maître un appareil **souvent allumé** et où MedReminder
  tourne.
- Sur les autres appareils, les demandes d'ordonnance s'ouvrent dans la
  messagerie, et **Tester la connexion** ne fonctionne que sur le
  maître. Les paramètres e-mail restent modifiables partout et
  atteignent tous les appareils.
- Un maître qui n'a pas synchronisé l'installation depuis **24 heures**
  cesse d'envoyer des e-mails jusqu'à sa prochaine synchronisation.
- Les installations publiées avant cette version n'ont pas de maître
  tant qu'un administrateur n'en choisit pas un ; d'ici là, chaque
  appareil envoie.

**Déplacer le maître vers un autre appareil**

1. **Outils → Installation… → Appareils**, sélectionne le nouvel
   appareil, **Définir comme maître…**, confirme. Aucun appareil
   n'envoie d'e-mail tant que le passage n'est pas terminé.
2. Sur le nouvel appareil, avec un profil administrateur ouvert, la
   fenêtre **Passage du rôle de maître** s'ouvre toute seule (ou plus
   tard depuis **Outils → Installation… → Terminer le passage de
   relais…**). Elle montre les paramètres et te demande de :
   - **Tester la connexion e-mail depuis cet appareil** ;
   - te **connecter** au stockage de la sauvegarde cloud avec le même
     compte ;
   - saisir de nouveau la **phrase de passe de la sauvegarde cloud**
     (elle n'est jamais copiée entre appareils) ;
   - saisir si besoin la phrase secrète de l'installation, pour apporter
     les profils qu'aucun appareil à portée de main ne contient.
3. **Confirmer**. Le nouvel appareil prend le relais quand l'ancien
   maître cède le rôle à sa prochaine synchronisation. Les nouveaux
   profils apparaissent au prochain démarrage.

**Le maître est en panne ou perdu.** Fais la même chose depuis un autre
appareil : le nouveau maître prend le relais tout seul quand l'ancien ne
s'est pas manifesté depuis 25 heures. Retire ensuite l'ancien
(ci-dessous).

<a id="remove-device"></a>
### Appareil perdu ou remplacé

Quand un appareil est perdu, vendu ou donné :

1. Sur le **maître** (il contient tous les profils) : **Outils →
   Installation… → Appareils**, sélectionne l'appareil, **Retirer
   l'appareil…**.
2. Choisis une **nouvelle phrase secrète de l'installation**. L'appareil
   retiré garde ce qu'il a déjà mais ne reçoit rien de nouveau ; les
   profils qu'il contenait reçoivent aussi de nouvelles clés.
3. MedReminder propose d'afficher un code. Les autres appareils cessent
   de se synchroniser jusqu'à ce qu'ils reçoivent la nouvelle clé : sur
   chacun, un administrateur ouvre **Outils → Installation… → Saisir la
   nouvelle clé…** et saisit la nouvelle phrase secrète ou le code. Les
   modifications faites entre-temps sont conservées.
4. Les nouvelles clés des profils arrivent toutes seules sur les autres
   appareils ; un profil ouvert à ce moment demande de redémarrer
   MedReminder.

Retirer le maître depuis un autre appareil fait de cet appareil le
maître. Déconnecte aussi l'appareil perdu de ton compte Microsoft ou
Google.

---

<a id="settings"></a>
## 10. Paramètres et usage quotidien

Tout se trouve dans **Outils → Paramètres…**. Les sections sont
listées à gauche ; **Ctrl+Tab** passe à la suivante. La fenêtre est
redimensionnable.

- **Général → Langue de l'interface** : anglais, italien, français,
  espagnol ou allemand. Les e-mails et la fiche de traitement
  l'utilisent aussi. MedReminder redémarre.
- **Général → Taille du texte (ce profil)** : Normale, Grande, Très
  grande, pour chaque profil. MedReminder suit aussi la mise à
  l'échelle et les thèmes de contraste de Windows. Sur un petit écran,
  préfère Grande.
- **Général → Apparence (ce profil)** : Comme Windows, Clair ou Sombre,
  pour chaque profil sur cet ordinateur. « Comme Windows » n'est sombre
  que sous Windows 11 avec le mode sombre activé ; avec un thème à
  contraste élevé de Windows, ses couleurs sont utilisées. S'applique
  après un redémarrage. En Sombre, les champs de date restent clairs.
- **Général → Vérifier les mises à jour de l'application et des
  catalogues (GitHub)** : recherche une nouvelle version au démarrage
  (rien n'est installé tout seul) et met à jour le catalogue au
  démarrage et une fois par jour. **? → Vérifier les mises à jour…**
  vérifie tout de suite.
- **Général → Journaliser les requêtes de la base de données
  (diagnostic)** : administrateurs uniquement. Écrit dans le fichier
  journal chaque commande de la base de données, sans les valeurs, pour
  le diagnostic. S'applique tout de suite ; le journal grossit vite,
  désactive-la ensuite.
- **Démarrage → Démarrer MedReminder à l'ouverture de session
  Windows** : démarre masqué dans la zone de notification. Aucun droit
  d'administrateur n'est nécessaire.

**Icône de la zone de notification.** Un double-clic ouvre la fenêtre ;
le clic droit propose *Ouvrir MedReminder*, *Vérifier maintenant*,
*Paramètres…*, *Quitter*.

**Soutenir le développement.** Si c'est activé, **? → Soutenir le
développement…** ouvre dans le navigateur une page de contribution
volontaire (Stripe ou PayPal). MedReminder ne voit jamais tes données de
paiement.

---

<a id="faq"></a>
## 11. Problèmes et réponses

**Je ne reçois pas d'e-mail.**
Vérifie dans l'ordre : *Tester la connexion* dans Paramètres → E-mail
SMTP ; le *Destinataire* dans Paramètres → Notifications ; le canal
**E-mail** coché sur le médicament ; MedReminder en cours d'exécution.
Avec une installation partagée, seul le maître envoie : regarde
**Outils → Installation… → État**.

**« Cet appareil est le maître mais n'a pas synchronisé l'installation
depuis plus de 24 heures ».**
Le maître n'atteint pas le stockage. Vérifie la connexion internet et la
connexion à OneDrive / Google Drive, puis **Synchroniser maintenant**.

**« La clé de l'installation a été changée sur un autre appareil ».**
Un appareil a été retiré. Ouvre **Outils → Installation… → Saisir la
nouvelle clé…** et saisis la nouvelle phrase secrète, ou un code
affiché par un appareil qui l'a déjà.

**« Un appareil a été retiré … ce profil a une nouvelle clé. Redémarrer
maintenant ? »**
Réponds oui : le profil prend sa nouvelle clé au redémarrage de
MedReminder.

**« Cet appareil a été retiré de l'installation ».**
L'appareil garde ses données mais ne reçoit plus rien. Pour le
réutiliser, un administrateur l'ajoute comme nouvel appareil.

**Le passage du rôle de maître ne se termine pas.**
L'ancien maître cède le rôle à sa prochaine synchronisation. S'il est
éteint pour de bon, le nouveau maître prend le relais 25 heures après la
dernière apparition de l'ancien.

**« Le groupe de synchronisation de ce profil appartient à une autre
installation ».**
Le profil a été publié depuis une autre installation. Rejoins cette
installation (**Rejoindre une installation existante…**).

**J'ai oublié un PIN.**
Un administrateur l'efface avec **Outils → Gérer les profils… → Changer
le PIN**. Si personne d'autre ne peut le faire : ferme MedReminder, ouvre
`%LOCALAPPDATA%\MedReminder\profiles.json` avec le Bloc-notes et, pour ce
profil, efface les valeurs de `PinHash` et `PinSalt` et mets
`PinIterations` à `0`. MedReminder enregistre la modification au
démarrage suivant ; avec une installation partagée, elle atteint les
autres appareils.

**J'ai oublié une phrase de passe ou phrase secrète.**
Celles de l'export, de la sauvegarde, de la synchronisation et de
l'installation ne peuvent pas être récupérées. Tu peux en définir de
nouvelles (nouvel export, nouvelle phrase de passe de sauvegarde cloud,
*Changer la clé et la phrase secrète…*), mais les fichiers chiffrés avec
l'ancienne restent illisibles.

**« Déjà en cours d'exécution ».**
MedReminder est déjà ouvert : cherche son icône dans la zone de
notification.

**Les données semblent endommagées.**
Restaure une sauvegarde (Paramètres → Sauvegarde / Restauration →
Restaurer une sauvegarde…) ou un export. Les fichiers journaux
(ci-dessous) aident à comprendre ce qui s'est passé.

---

<a id="data"></a>
## 12. Où MedReminder garde ses données

Tout se trouve dans `%LOCALAPPDATA%\MedReminder\` (colle-le dans la
barre d'adresse de l'Explorateur de fichiers). MedReminder n'écrit nulle
part ailleurs, sauf les fichiers de sauvegarde et d'export que tu places
toi-même.

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json              liste des profils, rôles, PIN (hachés)
├── smtp.settings.json         compte e-mail (sans mot de passe)
├── smtp.protected             mot de passe e-mail, chiffré par Windows
├── backup.settings.json       paramètres de sauvegarde
├── cloud-backup.protected     phrase de passe de la sauvegarde cloud, chiffrée par Windows
├── user.settings.json         langue, pays de référence, vérification des mises à jour, journal des requêtes
├── household\                 installation partagée (seulement si utilisée)
├── logs\medreminder-AAAAMMJJ.log
└── profiles\
    └── <profil>\
        ├── medreminder.db     médicaments et stock du profil
        ├── notifications.settings.json   destinataires
        ├── ui.settings.json   taille du texte, apparence, taille de la fenêtre
        └── sync.*             paramètres de synchronisation (seulement si utilisée)
```

- Les **journaux** enregistrent ce qu'a fait l'application
  (vérifications, e-mails envoyés, erreurs). Ils ne contiennent jamais
  de mots de passe, de texte d'e-mail ni de notes médicales.
- La base de données n'est pas chiffrée : elle est protégée par ton
  compte Windows. Les exports et les copies cloud sont chiffrés.
- **Mise à jour depuis une très ancienne version** (un seul
  `medreminder.db` directement dans le dossier) : le premier démarrage le
  déplace dans un profil nommé *User* et garde une copie dans
  `backups\pre-migration-…`, que tu peux supprimer quand tout est en
  ordre.

---

<a id="limits"></a>
## 13. Ce que MedReminder ne fait pas

- Il ne suit pas les doses prises et ne signale pas les doses oubliées
  (le rappel à l'heure de la prise n'est qu'un aide-mémoire).
- Il ne donne pas d'indications thérapeutiques et ne vérifie ni les
  doses ni les interactions médicamenteuses.
- Il ne commande pas de médicaments et ne contacte pas ton médecin tout
  seul.
- Il ne fusionne pas les données restaurées d'une sauvegarde : la
  restauration et l'importation remplacent toujours.

Son but est de te prévenir à temps qu'il te faut une nouvelle
ordonnance.
