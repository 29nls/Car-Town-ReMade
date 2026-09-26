<br/>
<p align="center">
  <a href="https://github.com/29nls/Car-Town-ReMade">
    <img src="https://cdn.myportfolio.com/0267835fdf119947cf36604d08f4f0e5/f2f4a76e840063b0c6271674.png?h=880d362c888fcbcd33215331990836aa" alt="Logo" width="645" height="362">
  </a>

  <br/>

  <a href="https://29nls.github.io/Car-Town-ReMade/">
    <img src="https://img.shields.io/badge/Play%20in%20browser-open%20the%20demo-2ea44f?style=for-the-badge" alt="Play in browser">
  </a>
  <a href="https://github.com/29nls/Car-Town-ReMade/actions/workflows/unity-ci.yml">
    <img src="https://github.com/29nls/Car-Town-ReMade/actions/workflows/unity-ci.yml/badge.svg?branch=main" alt="Unity CI status">
  </a>

  <h3 align="center"><a href="https://discord.gg/hKaMxECxQM"><img src="https://assets-global.website-files.com/6257adef93867e50d84d30e2/636e0b5061df29d55a92d945_full_logo_blurple_RGB.svg" width="500" height="50" alt="Join our Discord"></a></h3>

</p>

## About The Project (Alpha Stage)

![Screen Shot](https://media.discordapp.net/attachments/853588042790207489/1078791622180995102/xcvb.PNG)

Car Town was a social network game that let users collect and modify virtual cars. The game had different versions for Facebook, iOS and Android devices. The goal of the game was to win races and build a collection of cars. Car Town was popular among car enthusiasts who enjoyed customizing their vehicles, Which shut down in 2015. **We are asking people to help out the project and join the discord server for help**

## How To Run

This project uses **Unity 2020.1.8f1** (`22e8c0b0c3ec`), as recorded in `ProjectSettings/ProjectVersion.txt`. Newer Unity versions are not supported because upgrading the project can modify scenes, prefabs and materials.

### Requirements

* [Unity Hub](https://unity.com/download)
* Unity Editor **2020.1.8f1** (see [Unity download archive](https://unity.com/releases/editor/archive); the version is no longer listed in the default Unity Hub install list)

### Opening The Project

1. Clone or download this repository.
2. In Unity Hub open **Projects** and choose **Open > Add project from disk**, then select the folder that contains `Assets/`, `Packages/` and `ProjectSettings/`.
3. Wait for the first import to finish. Unity generates the missing `.meta` files for newly added scripts during this step, so let it complete before entering Play Mode.
4. Open `Assets/MainScene.unity` and press the **Play** button. Cars spawn from the Game Object that holds `CarSpawner` and follow the routes defined on the `Waypoints` object.
5. Optional: tweak `timeBetweenCars`, `prewarmCars` and `maxPoolSize` on the CarSpawner component in the Inspector to change traffic density.

This is a traffic simulation rather than an input-driven game, so there are no player controls; simply watch the scene run.

### Building A Standalone Player

1. Open **File > Build Settings**.
2. `MainScene` is already registered in the build settings, so no scene setup is required.
3. Select your target platform (for example PC, Mac & Linux Standalone) and install the matching platform module from Unity Hub if it is missing.
4. Press **Build** and choose an output folder.

### Notes About The Scene

The traffic light and pedestrian systems are implemented in code but are not placed in `MainScene` yet, and the scene currently has a single vehicle route. Add those objects (or a second parallel vehicle route) in the Editor to see those features in action.

## Continuous Integration

Every push to `main`, every pull request and every manual run of **Unity CI** executes [`.github/workflows/unity-ci.yml`](.github/workflows/unity-ci.yml) inside the official GameCI containers, so the runner needs no Unity installation. The badge at the top of this page reports the newest run on `main`; the runs themselves are listed under **Actions**.

| Job | What it does |
| --- | --- |
| `Publish Unity + Blender images` | Builds and publishes the custom editor image described below to GitHub's container registry. |
| `EditMode tests` | Runs the 25 tests in `Assets/Tests/EditMode` through `game-ci/unity-test-runner` and uploads the result file as an artifact. |
| `Compile & build project` | Builds a StandaloneLinux64 player. A build compiles every script, so compiler errors fail the job even though the player is not published. |
| `WebGL build & Pages deploy` | Builds the WebGL player and deploys it to GitHub Pages. Pull requests are skipped, so they validate the code without publishing a player. |

### The Unity + Blender image on GHCR

The repository contains `.blend` models, and Unity can only convert them when a `blender` executable is on `PATH`. `.github/docker/unity-editor-blender/Dockerfile` therefore installs Blender on top of a GameCI editor image, and the workflow publishes the result to GitHub's container registry under two tags - one per Unity module the jobs need:

    ghcr.io/<owner>/car-town-unity-ci:2020.1.8f1-linux-il2cpp-blender-<hash>
    ghcr.io/<owner>/car-town-unity-ci:2020.1.8f1-webgl-blender-<hash>

`<hash>` is the first eight characters of the SHA-256 of the Dockerfile, so the images are reused until that file changes. When publishing fails - for example because a private package exceeds the storage allowance of a free plan - the image job reports a warning instead of failing, and the other jobs fall back to the stock GameCI image: the build still runs, but the `.blend` models are skipped with "Blender could not be found". Making the packages public under **Packages** in the repository sidebar is free and keeps the Blender enabled image in use.

### Changing the Blender version

1. Update `BLENDER_SHORT_VERSION` (the release directory) and `BLENDER_FULL_VERSION` (the tarball) in `.github/docker/unity-editor-blender/Dockerfile`.
2. Commit and push. The Dockerfile hash in the image tag changes, so the next run rebuilds and republishes the image; nothing else has to be edited.

Keep the version at 3.3 or newer: Blender refuses to open files that were written by a newer version than itself, and the models here were saved by Blender 3.0 and 3.3. A rebuild takes a few minutes because the Unity base image is downloaded again.

### Running the EditMode tests locally

The 25 tests cover `CarPool` (14) and `SpatialHash` (11). They run against the `CarTown.Runtime` assembly and need no scene, so they finish in seconds.

In the Editor open **Window > General > Test Runner**, choose the **EditMode** tab and press **Run All**. From the command line, the runner accepts the same flags CI passes:

    "C:\Program Files\Unity\Hub\Editor\2020.1.8f1\Editor\Unity.exe" -runTests -batchmode -projectPath . -testPlatform EditMode -testResults TestResults.xml

Use `/Applications/Unity/Hub/Editor/2020.1.8f1/Unity.app/Contents/MacOS/Unity` on macOS or `~/Unity/Hub/Editor/2020.1.8f1/Editor/Unity` on Linux. The results are written to `TestResults.xml` in NUnit format, the same file the CI job reads.

### Unity license secrets

The Unity steps stay idle - with a warning, not a failure - until a license is available, so a fresh clone of this repository still gets green check marks. Add these under **Settings > Secrets and variables > Actions**:

| Secret | Value |
| --- | --- |
| `UNITY_LICENSE` | Contents of a manually activated license file (`.ulf`). For Personal licenses. |
| `UNITY_SERIAL` | License serial, used instead of `UNITY_LICENSE`. For Plus and Pro licenses. |
| `UNITY_EMAIL` | E-mail address of the Unity account. |
| `UNITY_PASSWORD` | Password of the Unity account. |

The one-time activation is described in the [GameCI activation guide](https://game.ci/docs/github/activation). Until the secrets exist no WebGL player is deployed, so <https://29nls.github.io/Car-Town-ReMade/> keeps returning 404.

### The WebGL player on GitHub Pages

The WebGL job builds with the template in `Assets/WebGLTemplates/CarTownReMade`: a branded loading screen with the project logo, a progress bar, a fullscreen button and a link back to this repository. `ProjectSettings/ProjectSettings.asset` selects it through `webGLTemplate`. The player is gzip compressed with **Decompression Fallback** enabled, because GitHub Pages cannot send `Content-Encoding` headers. Enable Pages once under **Settings > Pages** with **GitHub Actions** as its source; the workflow's `configure-pages` step only manages that when the token may administer Pages.

## License

Distributed under the MIT License. See [LICENSE.md](LICENSE.md) for more information.

## Acknowledgements

* [Zensai](https://github.com/zentoryny)
* [Clauzze](https://github.com/Clauzze)
* [Spamz](https://github.com/Spamzboi123)
