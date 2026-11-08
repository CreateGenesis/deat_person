# AR Digital Human MVP

Flutter + Unity + EasyAR based AR digital-human prototype. Unity owns the camera,
EasyAR localization, anchors, avatar rendering, animation, audio, and lip sync.
Flutter hosts the product UI and communicates with Unity. The backend provides
realtime conversation and provider configuration.

## Repository layout

```text
Assets/                 Unity scenes, scripts, avatar assets, and EasyAR settings
Packages/               Unity Package Manager manifest and local EasyAR packages
ProjectSettings/        Unity project settings
flutter_app/            Flutter Android host application
virtual-agent-backend/  Python API, realtime service, and administration web app
DOC/                    Architecture and integration documentation
architecture.md         High-level system architecture
```

Generated Unity caches, Flutter builds, APKs, local Unity exports, backend data,
and credentials are intentionally excluded from Git.

## Prerequisites

- Unity `6000.3.23f1`
- Flutter stable compatible with Dart `^3.13.0`
- Android SDK/NDK required by the Unity and Flutter versions
- Git LFS
- Python and Node.js versions described in `virtual-agent-backend`

Clone with submodules, then pull the large assets before opening Unity:

```powershell
git clone --recurse-submodules https://github.com/CreateGenesis/deat_person.git
cd deat_person
git lfs pull
```

## EasyAR configuration

The repository does not contain an EasyAR license key. Open the project in Unity,
select `Assets/XR/Settings/EasyAR Settings.asset`, and enter a license key issued
for the application's package identifier. Do not commit the key.

The EasyAR Unity packages are referenced from local package archives under
`Packages/EasyARSenseUnityPluginForMega_4003.1.0+5282.db47c2304/` and are stored
with Git LFS.

## Flutter host

```powershell
cd flutter_app
flutter pub get
.\tool\export_unity.ps1
flutter run
```

The Unity export is generated locally and is not committed. Re-run the export
script after changing Unity scenes, scripts, build settings, or native plugins.

## Backend

`virtual-agent-backend` is maintained as a Git submodule at
`CreateGenesis/virtual-agent-backend`. If it was not cloned automatically, run
`git submodule update --init --recursive`. Copy its `.env.example` to `.env`,
configure provider keys locally, and follow its README for development commands.
Never commit `.env` or provider credentials.

## Key documentation

- `architecture.md`
- `DOC/flutter-unity-integration.md`
- `DOC/flutter-unity-easyar-ai-digital-human-plan.md`
- `DOC/backend-integration/architecture.md`

