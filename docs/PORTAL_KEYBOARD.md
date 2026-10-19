# Clavier du portail (vue shell hébergée) — diagnostic

État : **corrigé** (relais `IInputObject::TranslateAcceleratorIO` + déclaration COM rectifiée).
Ce document conserve le diagnostic et la cause racine, pour ne pas repartir de zéro si une
régression apparaît.

Référence architecture : [PORTAL_EXPLORER_PARITY.md](PORTAL_EXPLORER_PARITY.md) (contrainte n° 2
« Focus » et n° 3 « Site COM et messages clavier »).

## 1. Symptôme (avant correctif)

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
   `DirectUIHWND`, fenêtre visible, activée, du thread UI. WPF ne les voit jamais via son
   `InputManager` : le problème n'est donc **pas** une interception WPF des routed events.
2. **↑ et ↓ font rendre le focus par la vue elle-même.** Pendant le traitement de la touche :
   `WM_ACTIVATE (WA_INACTIVE)` puis `WM_KILLFOCUS` sur `DirectUIHWND`, et `SetFocus` atterrit sur la
   **fenêtre racine WPF**. Pile WPF vide : c'est la vue qui appelle `SetFocus`.
3. **F2, Ctrl+A, Suppr sont traités dans le vide.** Keys livrées intégralement, aucun changement de
   focus, **aucun `WM_COMMAND` / `VCMD_*`** émis vers la fenêtre site.
4. **Identique dans tous les modes de vue** (liste, détails, icônes).
5. La **souris** traverse la même chaîne sans encombre : problème spécifique au routage clavier /
   accélérateurs.

## 3. Hypothèses testées et éliminées

| # | Hypothèse | Test | Verdict |
|---|-----------|------|---------|
| 1 | WPF reprend le focus au clic dans l'`HwndHost` | journal des `WM_SETFOCUS` de la fenêtre racine, avec pile WPF | éliminé : aucun vol mesuré après un clic |
| 2 | Focus donné trop tôt (pendant l'activation) | focus donné 2 s après ouverture | sans effet |
| 3 | `OleInitialize` jamais appelé sur le thread UI | valeur de retour : `S_OK` (et non `S_FALSE`) | **bug réel, corrigé** — nécessaire mais pas suffisant |
| 4 | Apartment COM / contexte d'activation | vérification STA, création par `CoCreateInstance` | conforme |
| 5 | WPF avale les touches avant la vue | écoute `ThreadPreprocessMessage` | les touches n'y passaient pas *comme accélérateurs* : il fallait les y **traduire** (voir §4) |
| 6 | Nos accélérateurs `Ctrl+C/X/V` gobbaient les messages | journal d'exécution des commandes | **bug réel, corrigé** (exécuté seulement si la vue rend une sélection non vide) |
| 7 | Navigation clavier WPF dans la fenêtre de portail | touches de navigation neutralisées sur le portail | sans effet |
| 8 | `WM_ACTIVATE` jamais relaied par WPF aux fenêtres hébergées | relais explicite vers la fenêtre site | **manque réel, corrigé** |
| 9 | Vue non activée au sens shell (`IInputObject::UIActivateIO`) | `QueryInterface` sur `IExplorerBrowser` | **faux négatif** : le GUID / la vtable de `IInputObject` étaient faux (voir §4) |
| 10 | Événements `IExplorerBrowserEvents` manquants | sink implanté | sans lien avec le clavier |
| 11 | Garde du focus | garde implantée, mesurée | inefficace sur les commandes ; optionnelle (`NAULTINUS_SHELLVIEW_GUARD=1`) |
| 12 | Géométrie / fenêtre masquée ou désactivée | rectangles, visibilité | conformes |
| 13 | Focus logique WPF bloquant | focus logique laissé sur l'hôte de vue | sans effet |

## 4. Cause racine et correctif

Deux défauts se cumulaient :

1. **Déclaration COM incorrecte de `IInputObject`.** Le spike utilisait le GUID
   `775E27B4-…` et une vtable `UIActivateIO` / `HasIOFocus` / `QueryObjects`. L'IID documenté est
   `68284FAA-6A48-11D0-8C78-00C04FD918B4` avec `UIActivateIO` / `HasFocusIO` /
   `TranslateAcceleratorIO`. Le QI renvoyait donc `E_NOINTERFACE` — d'où le faux verdict «
   `IExplorerBrowser` n'expose pas `IInputObject` ».
2. **Aucun appel à `TranslateAcceleratorIO` avant `DispatchMessage`.** Pattern établi (Windows API
   Code Pack, hôte MFC) : dans le filtre de messages du thread, pour chaque
   `WM_KEYFIRST`…`WM_KEYLAST` destiné à la vue, appeler
   `IInputObject::TranslateAcceleratorIO`. Sans cela les accélérateurs (F2, Ctrl+A, Suppr, ↑/↓…)
   ne partent jamais, alors que ←/→ et la souris passent par le WndProc.

Correctif dans `ShellFolderViewHost` :

- `IInputObject` corrigé (`ShellBrowserNative.cs`) ;
- `ComponentDispatcher.ThreadPreprocessMessage` → `HasFocusIO` / `UIActivateIO` / `TranslateAcceleratorIO` ;
- `FocusView` appelle `UIActivateIO` avec la bonne interface ;
- **↑/↓** : cause réelle mesurée = `HwndSource` → `KeyboardNavigation.Navigate` (WPF vole le
  focus pendant le KEYDOWN ; seule le KEYUP arrive à la vue). Correctifs : `DirectionalNavigation
  = None` sur le portail / l'hôte, filtre sur `ThreadFilterMessage` (avant `HwndSource`), et
  déplacement via `IFolderView::SelectItem` (±1).

## 5. Vérification

```powershell
.\scripts\run-portal-shellview.ps1 -Build -Keys
```

Jeu d'essai : clic sur un fichier → **↓ ↓ ↓** → **Ctrl+A** → **F2** puis **Échap** → **Suppr**.
Attendu : sélection qui bouge, tout sélectionné, renommage, suppression. Avec `-Keys`, le journal
peut montrer `TranslateAcceleratorIO a pris msg …`.

## 6. Boîte à outils de diagnostic

Journal : `%TEMP%\Naultinus_startup.log`.

| Variable | Effet |
|----------|-------|
| `NAULTINUS_SHELLVIEW=1` | corps de portail rendu par la vue shell |
| `NAULTINUS_SHELLVIEW_KEYS=1` | sonde clavier / focus / `WM_COMMAND` (`SondeClavier`) |
| `NAULTINUS_SHELLVIEW_GUARD=1` | rejoue la garde du focus (§3.11) |
| `NAULTINUS_SHELLVIEW_FOCUS=browser\|defview\|list` | cible du `SetFocus` de test |

## 7. Acquis conservés

- `OleInitialize` sur le thread UI avant de créer le navigateur (§3.3) ;
- relais de `WM_ACTIVATE` de la fenêtre WPF vers la fenêtre site (§3.8) ;
- accélérateurs `Ctrl+C/X/V` exécutés seulement si la vue rend une sélection (§3.6) ;
- focus logique du portail laissé à l'hôte de vue ;
- `IFolderView`/`IFolderView2` : sélection et renommage câblés côté managed.

## 8. Annexe hors clavier

`CheckModeChanged` lit un mode qui ne bouge pas quand l'utilisateur change d'affichage à la main —
à reprendre séparément.
