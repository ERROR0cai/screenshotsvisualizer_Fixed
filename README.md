基于Lacro59的[ScreenshotsVisualizer](https://github.com/Lacro59/playnite-screenshotsvisualizer-plugin)，做了个人修改：
- 修复了中文汉化不全的问题
- 新增外部刷新 API（由我修改后的[GameSnap_Fixed](https://github.com/ERROR0cai/GameSnap_Fixed)和[PlayniteMemories_Fixed](https://github.com/ERROR0cai/PlayniteMemories_Fixed)调用）
- 修复了当playnite游戏名包含非法字符时无法找到文件夹的bug。现在`{Name}`变量将自动移除非法字符但不压缩空格
