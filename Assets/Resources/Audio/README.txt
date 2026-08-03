环境音素材放置目录（触感层第一批，2026-08-03）

把 wav/mp3 丢进本目录、按下面的约定命名，运行时 AmbientAudio 自动装载：
  grass_rustle —— 点草簌簌声（短，1-2 秒，3D 定位播放，音量基准 0.4）
  gust_wind    —— 阵风滚过声（数秒，3D 定位播放，音量基准 0.35）

无素材时游戏静默不报错——宁可无声，不要难听。
音量/衰减约定见 Assets/Script/AmbientAudio.cs（MinDistance 2 / MaxDistance 20，克制）。
