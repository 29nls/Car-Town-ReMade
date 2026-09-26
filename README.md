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

## License

Distributed under the MIT License. See [LICENSE.md](LICENSE.md) for more information.

## Acknowledgements

* [Zensai](https://github.com/zentoryny)
* [Clauzze](https://github.com/Clauzze)
* [Spamz](https://github.com/Spamzboi123)
