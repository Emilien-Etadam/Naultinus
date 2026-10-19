# Clavier du portail (vue shell hébergée) — diagnostic

État : **non résolu**. La vue rendue par `IExplorerBrowser` dans le portail fonctionne à la souris,
pas au clavier. Ce document fige ce qui a été mesuré, ce qui a été éliminé, et les pistes qui
restent, pour ne pas repartir de zéro.

Référence architecture : [PORTAL_EXPLORER_PARITY.md](PORTAL_EXPLORER_PARITY.md) (contrainte n° 2
« Focus » et n° 3 « Site COM et messages clavier »). Ce document en est le retour d'expérience.

## 1. Symptôme

Dans une naultinus navigation ouverte sur un dossier (`SpikeDocs` du banc d'essai) :

| Action | Résultat mesuré |
|--------|-----------------|
| clic sur un fichier | sélection ✓ |
| double-clic, clic droit, glisser-déposer | ✓ (comportement Explorateur conservé) |
| double-clic espacé sur un fichier déjà sélectionné | renommage en place ✓ |
| **← / →** | ✓ la vue change de colonne |
| **↑ / ↓** | ✗ la sélection ne bouge pas ; la ligne « réagit » puis se fige |
| **F2**, **Ctrl+A**, **Suppr**, **Ctrl+C / X / V**, **Entrée** | ✗ aucun effet |
| saisie du début du nom (type-ahead) | ✗ |

## 2. Ce qui a été mesuré (niveau messages Windows)

Sous-classement des 8 fenêtres de la vue (`NAULTINUS_SHELLVIEW_KEYS=1`, catégorie
`SondeClavier` du journal `%TEMP%\Naultinus_startup.log`).

Chaîne de fenêtres relevée (`ArbreFenetres`) :

```text
(fenêtre racine WPF 0x1506C2)
└─ NaultinusShellViewSite 0xF1010        fenêtre site de notre HwndHost
   └─ ExplorerBrowserControl 0x4019F0    900x540
      └─ SHELLDLL_DefView 0x3F1A26       898x538
         └─ DirectUIHWND 0x171A2E        898x538   ← fenêtre qui reçoit les touches
            ├─ CtrlNotifySink 0x561D82   (masquée)
            ├─ ScrollBar 0xF0716         (masquée)
            ├─ CtrlNotifySink 0x191DB4   17x538
            └─ ScrollBar 0x1A1E1A        17x538
```

1. **Les touches parviennent bien à la vue.** `WM_KEYDOWN` **et** `WM_KEYUP` sont livrés à
   `DirectUIHWND`, fenêtre visible, activée, du thread UI (`GetWindowThreadProcessId` = thread UI).
   WPF ne les voit jamais : `ComponentDispatcher.ThreadPreprocessMessage` ne reçoit ni les flèches
   ni F2. Le problème n'est donc **pas** une interception WPF.
2. **↑ et ↓ font rendre le focus par la vue elle-même.** Pendant le traitement de la touche :
   `WM_ACTIVATE (WA_INACTIVE)` puis `WM_KILLFOCUS` sur `DirectUIHWND`, et `SetFocus` atterrit sur la
   **fenêtre racine WPF** (`WM_SETFOCUS wParam = 0x1506C2`). Pile WPF vide à ce moment : c'est la
   vue qui appelle `SetFocus`, aucun code WPF n'est en cause. Il ne reste que le `WM_KEYUP`, et la
   navigation est perdue. C'est le comportement d'une vue qui se croit **au bord de son panneau** et
   qui passe le focus à son cadre (`IShellBrowser::SendMoveFocus`) — cadre que nous n'avons pas.
3. **F2, Ctrl+A, Suppr sont traités dans le vide.** Keys livrées intégralement, aucun changement de
   focus, **aucun `WM_COMMAND` / `VCMD_*`** (VCMD_SELECTALL 0x7D02, VCMD_RENAME 0x7D15,
   VCMD_DELETE 0x7D05, VCMD_SETFOCUS 0x7D06…) émis vers la fenêtre site. La table de décodage est
   en place dans `ShellFolderViewHost.CommandName` : elle n'a jamais rien décodé d'autre qu'un
   `0x7050` du menu contextuel.
4. **Identique dans tous les modes de vue** (liste, détails, icônes) : le mode n'explique rien.
   (Au passage, la surveillance de mode `CheckModeChanged` lit une valeur qui ne bouge pas quand
   l'utilisateur change de mode — à reprendre, indépendamment du clavier.)
5. La **souris** traverse la même chaîne de fenêtres sans encombre : le problème est spécifique au
   routage clavier / commandes.

## 3. Hypothèses testées et éliminées

| # | Hypothèse | Test | Verdict |
|---|-----------|------|---------|
| 1 | WPF reprend le focus au clic dans l'`HwndHost` | journal des `WM_SETFOCUS` de la fenêtre racine, avec pile WPF | éliminé : aucun vol mesuré après un clic ; les vols enregistrés viennent d'autres zones WPF (zone de titre du portail, boîte de dialogue de mise à jour au démarrage) |
| 2 | Focus donné trop tôt (pendant l'activation) | focus donné 2 s après ouverture | sans effet |
| 3 | `OleInitialize` jamais appelé sur le thread UI | valeur de retour : `S_OK` (et non `S_FALSE`) | **bug réel, corrigé** — nécessaire mais pas suffisant |
| 4 | Apartment COM / contexte d'activation | vérification STA, création par `CoCreateInstance` | conforme |
| 5 | WPF avale les touches avant la vue | écoute `ThreadPreprocessMessage` | éliminé (voir §2.1) |
| 6 | Nos accélérateurs `Ctrl+C/X/V` gobbaient les messages | journal d'exécution des commandes | **bug réel, corrigé** (exécuté seulement si la vue rend une sélection non vide) |
| 7 | Navigation clavier WPF dans la fenêtre de portail | touches de navigation neutralisées sur le portail | sans effet (elles ne recevaient rien) |
| 8 | `WM_ACTIVATE` jamais relaied par WPF aux fenêtres hébergées | relais explicite vers la fenêtre site | **manque réel, corrigé** — la vue est bien activée (`WA_ACTIVE` mesuré) mais le clavier ne revient pas |
| 9 | Vue non activée au sens shell (`IInputObject::UIActivateIO`) | `QueryInterface` sur `IExplorerBrowser` | **`E_NOINTERFACE`** : la route n'existe pas ici, `IExplorerBrowser` n'expose pas `IInputObject` |
| 10 | Événements `IExplorerBrowserEvents` manquants | sink réellement implanté (27 entrées de vtable) | la vue change de mode, **aucun événement reçu** : sink non branché. Sans lien avec le clavier |
| 11 | Garde du focus (rendre le focus à la vue dès que WPF le reprend) | garde implantée, mesurée | sans effet sur les commandes ; la garde se bat avec la vue. Désactivée par défaut (`NAULTINUS_SHELLVIEW_GUARD=1` pour la rejouer) |
| 12 | Géométrie / fenêtre masquée ou désactivée | rectangles, `IsWindowVisible`, `IsWindowEnabled`, fenêtre non layered | conformes (898x538, visible, activée) |
| 13 | Focus logique WPF bloquant | focus logique laissé sur l'hôte de vue | sans effet |

## 4. Hypothèse principale restante

**La vue hébergée n'a pas de cadre OLE exploitable.** Dans l'Explorateur, `SHELLDLL_DefView` vit
sous `WorkerW`, dont la procédure répond aux contrats du cadre : `IShellBrowser` (dont
`SendMoveFocus`, `QueryActiveShellView`, `OnViewWindowActive`, `GetControlWindow`), `IOleWindow`,
`IOleInPlaceActiveObject::TranslateAccelerator`, `IOleCommandTarget`. Notre spike n'en fournit
aucun — c'était une limite assumée (« pas de `IShellBrowser` parent », §Limites du spike de
[PORTAL_EXPLORER_PARITY.md](PORTAL_EXPLORER_PARITY.md)).

Les deux symptômes collent exactement :

- ↑/↓ au bord du panneau → *move focus* vers le cadre → intraçable → focus balancé sur la fenêtre
  de premier niveau ;
- F2 / Ctrl+A / Suppr → commandes de vue qui doivent être **transmises par le conteneur** (via
  `IOleInPlaceActiveObject::TranslateAccelerator` pour les accélérateurs, `IOleCommandTarget::Exec`
  pour les commandes) → personne ne les transmet → rien ne part, et aucun `VCMD_*` n'est émis.

Notre fenêtre site n'implémente à ce jour que `IOleWindow` (+ `IExplorerBrowserEvents`). Les
interfaces `IOleClientSite`, `IOleInPlaceSite`, `IOleInPlaceFrame`, `IInputObjectSite` sont absentes,
et `TranslateAccelerator` n'est appelé nulle part dans la boucle de messages WPF.

## 5. Pistes, dans l'ordre de rentabilité

1. **Traduire les messages d'accélérateur vers l'objet en place actif.** Dans
   `ComponentDispatcher.ThreadPreprocessMessage` (ou un `HwndSource.AddHook` sur la fenêtre de
   portail), pour chaque `WM_KEYDOWN`/`WM_SYSKEYDOWN` : obtenir l'objet actif en place de la fenêtre
   du navigateur (`GetWindowInfo(GWI_HWNDACTIVE)` sur la fenêtre de premier niveau, ou
   `IObjectWithSite`/QI `IOleInPlaceActiveObject` sur `IExplorerBrowser`) et appeler
   `TranslateAccelerator(&msg)`, puis `IOleCommandTarget::Exec` sur `CGID_ShellCommands`.
   Coût : faible (une dizaine de lignes + 3 déclarations d'interface). **À tenter en premier.**
2. **`IInputObjectSite` sur l'objet donné à `IExplorerBrowser::SetSite`** (une seule méthode,
   `OnFocusChangeIS`) : le navigateur n'arme peut-être sa machine à focus que si le site la
   fournit. Coût très faible.
3. **Héberger dans une fenêtre de premier niveau** (`WS_POPUP` possédée par le portail) plutôt
   qu'en fenêtre enfant de la fenêtre WPF. Coût : une demi-journée ; réponse nette à « est-ce que
   WPF (airspace / arbre clavier) est en cause ? ». Si le clavier fonctionne ainsi, on garde la
   fenêtre de premier niveau et le problème est clos.
4. **Remplacer `IExplorerBrowser` par `CLSID_ShellView` + notre `IShellBrowser`** (la voie de
   l'Explorateur). C'est la solution « correcte », mais elle demande d'implanter ~28 méthodes de
   vtable dont le comportement exact n'est documenté que partiellement.
5. **Clavier pris en charge en code managé** (plan B, toujours possible et non exclusif) : le
   portail garde le focus WPF et traduit les touches en appels `IFolderView2` / `IShellView` /
   `IFileOperation` — sélection ↑/↓, `F2` par `IShellItem::EditName` ou `SHDefEdit`, `Entrée` par
   `IExplorerCommand`/`InvokeCommand`, `Ctrl+C/X/V` et `Suppr` par `IFileOperation`, `Ctrl+A` par
   `IFolderView2::Select(DFMESS_SS)`. Prévisible, mais il faut réécrire une douzaine de
   comportements (dont le type-ahead) et on perd les nuances de la vue native.

## 6. Boîte à outils de diagnostic (déployée)

Journal : `%TEMP%\Naultinus_startup.log` sur le poste Windows (catégories citées ci-dessous).

| Variable | Effet |
|----------|-------|
| `NAULTINUS_SHELLVIEW=1` | corps de portail rendu par la vue shell (sinon grille WPF) — obligatoire pour le spike |
| `NAULTINUS_SHELLVIEW_KEYS=1` | sous-classe les 8 fenêtres de la vue et journalise clavier, activation, focus, `WM_COMMAND` (catégorie `SondeClavier`), plus l'arbre de fenêtres (`ArbreFenetres`) |
| `NAULTINUS_SHELLVIEW_GUARD=1` | rejoue la garde du focus (§3.11) |
| `NAULTINUS_SHELLVIEW_FOCUS=browser\|defview\|list` | fenêtre à laquelle le focus est donné lors des tests de focus |

Commandes de jeu d'essai, dans l'ordre : clic sur un fichier → **↓ ↓ ↓** → **Ctrl+A** → **F2** puis
**Échap** → **Suppr**. Les lignes attendues et leur lecture sont celles du §2.

Lancement depuis Windows (construction WSL, exécution sur copie locale) :
`scripts/run-portal-shellview.ps1 -Build -Keys` (voir §Lancement de
[PORTAL_EXPLORER_PARITY.md](PORTAL_EXPLORER_PARITY.md) pour les règles de lancement).

## 7. Acquis conservés (ne pas refaire)

Ces corrections sont réelles, indépendantes du clavier, et restent en place :

- `OleInitialize` sur le thread UI avant de créer le navigateur (§3.3) — sans lui, presse-papiers,
  dépôt et renommage en place sont hors d'état ;
- relais de `WM_ACTIVATE` de la fenêtre WPF vers la fenêtre site (§3.8) ;
- accélérateurs `Ctrl+C/X/V` exécutés seulement si la vue rend une sélection (§3.6) ;
- focus logique du portail laissé à l'hôte de vue (pas de navigation WPF parasite dans le portail) ;
- `IFolderView`/`IFolderView2` : `SelectItem`, `GetSelection`, renommage et sélection totale
  câblés côté managed — utilisables directement par la piste 5.