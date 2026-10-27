# 人物渲染：Scene / Game 差异与星铁方案调查

调查日期：2026-10-14。项目环境为 Unity 6000.6.3f1 / URP 17.6。当前结论是先完善人物材质输入和受光控制；局部几何间接光能够改善空间关系，但不能代替人物的阴影配色与高光设计。

## Scene View 为什么更亮

当前实验是运行期方案，默认只服务 `ExperimentalRenderSession.RenderCamera`，即 `CameraOverride` 或 `Camera.main`。Unity 的 Scene 相机不是这台相机。

| 处理 | Game 目标相机 | Unity Scene 相机 |
| --- | --- | --- |
| 人物实验受光与各类高光 | 在 `ExperimentalSurfaceLighting.BeginCamera` 设置全局参数，渲染后清除 | 沿用角色 shader 的原分支 |
| 多方向光处理 | 有有效 `RenderSettings.sun` 时，将其他方向光临时缩放到 `SecondaryDirectionalStrength` | 使用原灯光强度 |
| 曝光 / Neutral 色调映射 | 目标相机启用后处理并纳入实验 Volume 层，默认曝光 -0.4 EV | 未被实验代码配置；还受 Scene 自身后处理开关影响 |
| 实验体积雾 / 体积光 | 目标 Base 相机执行体积计算和合成 | Renderer Feature 的相机筛选跳过 |
| 局部几何间接光 / 接触遮蔽 | 目标相机更新、绑定三维光照缓存 | 不绑定实验局部光照参数 |

因此，截图中 Scene 的天空和人物更亮、Game 更灰，并非同一次渲染输出的差异。两张截图的相机位置、视角和输出比例也不同。只打开 Scene 的后处理开关无法补齐实验受光和体积效果。

运行期场景反射 cubemap 和临时 Lit 材质副本也可能被 Scene 看见，所以 Scene 不是一张完全独立的原方案对照图。可靠的 A/B 应在 Game 的同一相机、同一姿态下改代码配置，等 shader 编译和缓存刷新完成，再逐项比较 `Surfaces`、`Settings.Density` 和 `LocalLighting`。

若以后需要一致的 Scene 预览，应让 Scene 相机经过同样的受光、显示变换与体积处理，并明确其缓存所有权；不能只打开全局 shader 参数，也不能让两个相机反复移动同一份局部缓存。目前尚未实现完整的 Scene 预览。

## 本项目目前的人物输入

测试场景为 `2CHAN/1526794756848.png`，包含 7 个角色。既有导入审计中有 249 份 `StudioEditor/AnimeCharacter` 材质；它们的 `_RampStrength` 和 `_StyleMaskStrength` 均为 0。因此这些人物主要使用通用 toon 分段，而不是来源材质专用的 Ramp / LightMap。材质审计发生在此前的表面实验之前；当前实验可以通过相机全局参数增加按角色类型划分的高光，不能据此断言当前高光仍全部为 0。

`DefaultAnimeCharacterRenderScheme` 目前已区分 Skin / Face / Hair / Clothes / Accessory，复制部分法线、高光与颜色参数。`CharacterRenderMaterialContext` 对兼容的 Toon Ramp 有明确输入，但没有明确的 LightMap 通道布局契约；目前仍按属性名查找 `_LightMap` 等贴图。

恋活的身体、脸部、眼睛、衣服在导入期间先合成颜色、妆容、图案或遮罩贴图，再创建渲染材质。这些合成结果是表面颜色，不是已求解的场景间接光。颜色贴图与控制贴图必须继续分开。

## dnSpy MCP 核查的恋活原版行为

通过本机 dnSpy MCP 读取已加载的 `Assembly-CSharp`，核查了以下方法：

| 方法 | 已确认行为 |
| --- | --- |
| `ChaControl.LoadHairGlossMask` | 根据角色卡 `hair.glossId`，从 `mt_hairgloss` 分类的 `MainTexAB` / `MainTex` 读取贴图 |
| `ChaControl.ChangeSettingHairGlossMask` | 将上述贴图写入各头发 Renderer 的 `_HairGloss` |
| `ChaControl.ChangeSettingHairColor` | 将角色卡的 base / start / end 颜色分别写入 `_Color` / `_Color2` / `_Color3` |
| `ChaShader.ChangeRampTexture` | 将 Ramp 贴图写入全局 `_RampG` |
| `Studio.SystemButtonCtrl.EtcInfo.SetRamp` | 由 `sceneInfo.rampG` 选择 `mt_ramp` 分类贴图，再设置全局 `_RampG` |
| `Studio.SystemButtonCtrl.EtcInfo.SetAmbientShadow` | 将 `sceneInfo.ambientShadow` 的 alpha 设为 `1 - ambientShadowG`，写入全局 `_ambientshadowG` |

2026-10-15 复查原版 `ChaListDefine.CategoryNo` 后确认：`mt_pattern=430`、`mt_emblem=431`、`mt_ramp=432`、`mt_hairgloss=433`。原导入代码误用了 439，导致本轮场景卡的 `glossId=3` 查找失败，头发材质的 `_UseHairGloss=0`；已修正为 433。身体和头发的控制参数并不等于星铁的控制贴图。

上述证据确认的是资源选择与参数赋值。原 shader 的采样坐标、通道含义和最终着色公式仍需要检查 bundle 中的 Shader；dnSpy 对托管程序集的反编译不能直接恢复 GPU shader。检索原程序集没有找到 `_LightMap` 或 `ShadowExtend` 字符串，也不能据此断言所有 mod 材质都没有这些参数。

## 社区星铁实现的可借鉴部分

主要参考 [stalomeow/StarRailNPRShader](https://github.com/stalomeow/StarRailNPRShader)，核查版本提交 `bf44567f389834b8be5e3095e5ff507caa8764fb`；同时阅读 [Gaolingx/HonkaiStarRailToonShader](https://github.com/Gaolingx/HonkaiStarRailToonShader)，核查提交 `c9b9f8d41f75a01664365ab91228fea8ec9dee79`。它们是社区仿制方案，不是米哈游官方管线。

| 结构 | 社区实现 | 当前项目需要补的内容 |
| --- | --- | --- |
| 材质分区 | Body 使用 LightMap alpha 选择最多 8 组参数；Hair 独立处理 | 在来源材质明确的前提下传入区域数据，缺失时按角色类型提供默认值 |
| 阴影颜色 | 用专门的冷 / 暖 Ramp 控制各材质的明暗色；AO 影响过渡位置 | 保留肤色、头发色与布料颜色，明确 Ramp 布局和转换规则 |
| 高光 | LightMap 强度 / 阈值通道配合每组参数，控制高光位置与边界 | 用已验证的恋活高光数据，而不是把所有表面套同一组阈值 |
| 附加灯 | 与主光分开处理，避免多盏灯简单相加洗掉材质颜色 | 检查当前附加 toon 分段累加，维持主光明暗层次 |
| 显示变换 | 使用定制后处理和 Tonemapping | 同一显示变换下比较受光，避免曝光差异掩盖材质问题 |

具体通道差异可以在 [CharRenderingHelpers.hlsl](https://github.com/stalomeow/StarRailNPRShader/blob/bf44567f389834b8be5e3095e5ff507caa8764fb/Shaders/Character/Shared/CharRenderingHelpers.hlsl) 与 [CharBodyMaterials.hlsl](https://github.com/stalomeow/StarRailNPRShader/blob/bf44567f389834b8be5e3095e5ff507caa8764fb/Shaders/Character/CharBodyMaterials.hlsl) 核对：

| 通道 | 上述星铁 Body LightMap | 当前 StudioEditor StyleMask |
| --- | --- | --- |
| R | 高光强度 | 没有按此含义使用 |
| G | AO / 阴影过渡控制；游戏模型还结合顶点 R | Ramp 的行坐标 |
| B | 高光阈值 | 金属度遮罩 |
| A | 材质编号，选择 8 组参数 | 高光强度遮罩 |

这些贴图不能直接互换。即使来源属性叫 `_LightMap`，也需要适配器明确其 shader 和通道布局。恋活顶点色还可能承载头发或衣服颜色混合，不应直接当成星铁的顶点 AO。

社区实现还含有基于屏幕深度的边缘光、刘海深度处理和屏幕空间阴影 pass。它们不纳入本项目当前实验；面部 SDF 继续暂缓。可借鉴的材质分区、Ramp 与高光计算不依赖屏幕空间遮蔽。

参考包声明的主要环境是 Unity 2022.3 / URP 14，不能据此认为整包在本项目 URP 17.6 的 RenderGraph 中可直接运行。stalomeow 包为 GPL-3.0；Gaolingx 仓库虽标为 MIT，但包清单和使用说明依赖 stalomeow 包。本轮仅研究结构，没有引入其运行代码、材质或模型。

## 后续适配顺序

1. 在同一 Game 相机下固定显示变换，分别观察原受光、实验受光、雾和局部间接光。先用近景检查人物颜色与明暗，不用整张场景的灰度判断单个材质。
2. 在恋活适配器增加明确的来源 shader 配置，记录并验证 Ramp / Gloss / 控制贴图的布局。未知 shader 继续回退通用 toon；不按相似属性名猜测。
3. 扩展独立人物实验方案的数据契约：冷暖阴影配色、材质区域、局部 AO、高光强度 / 阈值。优先沿用已验证的 HairGloss 与原卡颜色；缺少区域图时使用按角色类型的默认配置。
4. 分别调整头发、皮肤、衣服的阴影色与高光，并检查附加灯的能量累加。脸部保留当前法线方案，先处理肤色和受光强度。
5. 将几何间接光作为上述材质的环境输入，接触遮蔽只约束对应的间接和直射成分。最后再合上体积效果观察整体。

既有 [RenderSandboxDesign.md](RenderSandboxDesign.md) 关于恋活 Ramp 兼容的约束继续有效：不同来源 shader 的 LUT 不可按名称直接复用。本轮已完成局部几何光照基础和上述调查，专门的人物材质适配仍待实现。
