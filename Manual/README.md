# Kimodo Unity Bridge 使用手册

> 适用版本：KimodoUnityBridge v1.1.39

开箱即用、完全运行在本地的 AI 人形动画生成系统。你只需输入提示词、放置约束、点击生成，就能在 Unity 里得到想要的角色动画。

这份手册是所有说明文档的入口。下面按你的使用场景，分别指向对应的分册。

<!-- 这里可以放一张插件整体界面的截图 -->



## 从这里开始

如果你是第一次接触本插件，建议按这个顺序读：

1. 先看 [Kimodo Server Manager](Kimodo%20Server%20Manager%20说明书.md)，把本地运行环境和模型准备好。
2. 再看 [Timeline Tool](Timeline%20Tool%20说明书.md)，跑通"写提示词 → 生成 → 播放"的最基础流程。
3. 之后按需深入约束、状态机、运行时等进阶用法。

遇到报错随时翻 [常见问题与报错处理](常见问题与报错处理.md)。



## 分册目录

### 生成工具

- **[Timeline Tool](Timeline%20Tool%20说明书.md)** — 在时间轴上生成动画的基础玩法，包含长动画、循环、过渡的组合思路。
- **[Animator Tool](Animator%20Tool%20说明书.md)** — 直接在状态机里替换某个状态的动作，或为两个状态之间插入衔接动画。
- **[Constraint Tool](Constraint%20Tool%20说明书.md)** — 用约束 Marker 精确控制某一帧的姿势、手脚位置和移动轨迹。

### 配置与运行时

- **[Kimodo Server Manager](Kimodo%20Server%20Manager%20说明书.md)** — 本地服务器、模型管理与全局选项的控制台（位于 Project Settings）。
- **[Runtime 配置与 API](Runtime%20配置与%20API%20说明书.md)** — 让发布版游戏在运行时实时生成动画，含 InfiniteMotionDemo 配置与代码接口。

### 排查问题

- **[常见问题与报错处理（QA）](常见问题与报错处理.md)** — 按场景分组的常见报错与解决方案。



## 环境要求

- Unity 2021 及以上，支持 Windows、macOS、Linux 平台。
- 内存 ≥ 8G，硬盘可用空间 ≥ 10G。
- NVIDIA 显卡显存 ≥ 6G 时可运行 CUDA 版本（不做强制限制，CPU 也能跑，只是更慢）。



## 提交反馈

如果遇到本手册没有覆盖的问题，欢迎提交日志帮助改进，具体方式见 [常见问题与报错处理](常见问题与报错处理.md) 的"提交 Bug"一节。
