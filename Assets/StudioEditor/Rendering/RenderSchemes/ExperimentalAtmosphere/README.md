# URP 场景渲染实验

此目录提供独立的场景级实验方案。切换入口只有 `ExperimentalRenderConfiguration.cs`，没有新增 UI。当前默认启用实验；进入 Play Mode 后自动创建运行期管线副本，目标相机默认跟随 `Camera.main`。

Unity Scene View 尚未走完整的实验受光、显示变换和体积效果，因此不能用它与 Game 直接比较亮度。具体相机差异、恋活原版参数与社区星铁材质适配调查见 [人物渲染调查](../../StarRailCharacterStudy.md)。

## 代码开关

修改 `ExperimentalRenderConfiguration.Scheme` 的初始值，再进入 Play Mode：

```csharp
// 原方案
public static SceneRenderScheme Scheme = SceneRenderScheme.DefaultAnime;

// 体积效果实验
public static SceneRenderScheme Scheme = SceneRenderScheme.ExperimentalAtmosphere;
```

`Settings` 的初始值也在这个文件里。运行期可修改配置，下一帧生效：

```csharp
ExperimentalRenderConfiguration.Settings.Shadows = AtmosphereShadowMode.HardwareRayTracing;
ExperimentalRenderConfiguration.Settings.Quality = AtmosphereQuality.Balanced;
ExperimentalRenderConfiguration.Settings.Density = 0.018f;
ExperimentalRenderConfiguration.Settings.MaxDistance = 60f;

// 回切
ExperimentalRenderConfiguration.Scheme = SceneRenderScheme.DefaultAnime;
```

## 表面受光实验

`ExperimentalRenderConfiguration.Surfaces` 控制人物与场景表面受光，可与雾单独对照：

```csharp
// 只比较体积效果，沿用原人物受光
ExperimentalRenderConfiguration.Surfaces.Enabled = false;

// 启用表面受光实验
ExperimentalRenderConfiguration.Surfaces.Enabled = true;
ExperimentalRenderConfiguration.Surfaces.SecondaryDirectionalStrength = 0.2f;
ExperimentalRenderConfiguration.Surfaces.SceneReflectionProbe = true;
```

表面方案保留原 toon 明暗分段，将主光遮挡与明暗分段分开计算。角色附加灯现在使用带阴影的 URP 采样；头发、皮肤、衣服、配件的高光强度从导入时的明确角色类型选择，脸部暂不新增高光。非金属也可以按 Fresnel 接收环境反射，不再要求金属度大于 0。

源身体皮肤（model 3）保留导入 Ramp 的明暗分段，并用导入材质的 `_ShadowColor` 限制源阴影端点，避免原环境叠色将暗部抬得接近白色。实验身体接收主光、附加灯的原生影图时，使用单次双线性深度比较，不叠加宽 PCF；灯光强度、阴影强度及附加灯的距离淡出仍保留。服装投到身体的专用影图也只保留一组双线性比较。面部、眼睛、头发、服装本体和默认方案不应用这项身体影图采样。源身体高光仍走其独立材质参数，不受通用 `SkinHighlight` 控制。

| `Surfaces` 参数 | 作用 |
| --- | --- |
| `DirectStrength` / `AmbientStrength` | 人物主光强度、环境球谐光强度 |
| `LightColorInfluence` | 人物受光颜色影响程度 |
| `HairHighlight` / `SkinHighlight` / `ClothHighlight` / `AccessoryHighlight` | 各类表面的高光强度 |
| `ReflectionStrength` | 人物环境反射强度；脸部和扁平细节仍跳过 |
| `SecondaryDirectionalStrength` | 当 `RenderSettings.sun` 有效时，其余方向光的倍率；设为 1 可保留所有方向光的原强度 |
| `SceneReflectionProbe` / `ReflectionResolution` / `ReflectionDistance` | 实际场景 cubemap 捕获开关、分辨率、作用范围 |
| `ToneMapping` / `Exposure` | Neutral 色调映射、以 EV 为单位的曝光补偿，默认 -0.4；处理 HDR 显示，不负责求解光照 |

地图自带的方向光和编辑器默认方向光经常同时存在。倍率只在目标相机和实验反射相机渲染期间应用，渲染结束后恢复 Light 的原强度；不会改写保存的场景数据。没有指定有效主光时不调整其他灯。局部点光和聚光保留原强度。

场景反射默认分辨率 128，通过 URP `SingleCameraRequest` 显式捕获六个方向，并绑定到运行期 Custom ReflectionProbe。每帧捕获两个方向，完整 cubemap 才发布，保留上一份完整结果。相机移动超过 4 米、目标相机切换、光追几何数量变化、主光方向/强度变化、捕获配置变化或显式调用 `RefreshSceneReflection()` 后重新捕获，捕获之间至少间隔 2 秒。它包含镜头外物体，不使用 SSR。当前使用普通 mipmap 近似粗糙反射，尚未做专门的 GGX 预过滤；也不能替代光追反射和局部遮挡 GI。移动物体的反射需要主动刷新，复杂场景会增加捕获开销。

项目原质量设置关闭了 realtime reflection probes；这里使用显式捕获与 Custom probe，不依赖该开关。色调映射使用独立运行期 Volume 和目标相机的后处理配置，避免 HDR 受光值直接被截成纯白；回切恢复相机的原设置并销毁实验 Volume。反射捕获保留 HDR 辐射值，不叠加显示曝光或雾。

场景导入也修正了 URP fallback 的信息损失：显式法线、金属度/镜面工作流、光滑度、遮蔽贴图和自发光可传入 Lit，保留贴图及 UV 参数。这个导入修复对两种渲染方案都生效，已有加载对象需重新导入；不会凭空生成源资源中没有的法线或粗糙度，也不推断自定义打包纹理的未知通道。URP Lit 使用共用基础 UV，源 shader 独立分层 UV 和复杂混合尚未完整还原。

关闭 Domain Reload 时，静态配置会保留上次运行期的值。需要立即切换时，可调用 `ExperimentalRenderSession.Current.ApplyScheme(...)`。诊断可读取会话的 `Status`、`HardwareShadowsActive`、`RayTracingInstances`。自定义相机可赋给 `CameraOverride`；需要马上更新几何时调用 `RefreshRayGeometry()`。

## 局部间接光与接触遮蔽

面部的自身明暗由 SDF 控制，同角色对面部的投影只来自经过筛选的头发。面部场景影图排除整个所属角色，身体／颈部、服装、舌头和普通配件不再投到自己的脸上；房间及其他角色仍可遮挡面部和眼睛。服装皮肤影图只绑定身体皮肤，Shader 也拒绝其作用于面部，身体上的服装接触阴影继续保留。

`ExperimentalRenderConfiguration.LocalLighting` 默认启用。它直接对场景模型发射几何射线，不使用屏幕颜色、屏幕深度、SSGI、SSR 或 SSAO。即使雾的 `Density` 为 0，也可独立运行；关闭 `Surfaces.Enabled` 或回切原方案会一起释放它。

```csharp
// 对照局部光照开关
ExperimentalRenderConfiguration.LocalLighting.Enabled = false;
ExperimentalRenderConfiguration.LocalLighting.Enabled = true;

// 单独调整一跳反弹光、近处遮蔽、主光接触阴影
ExperimentalRenderConfiguration.LocalLighting.IndirectStrength = 0.8f;
ExperimentalRenderConfiguration.LocalLighting.OcclusionStrength = 0.7f;
ExperimentalRenderConfiguration.LocalLighting.ContactShadowStrength = 0.65f;

// 代码诊断视图：None / Indirect / Occlusion / ContactShadow / Coverage
ExperimentalRenderConfiguration.LocalLighting.DebugView = LocalLightingDebugView.Occlusion;
ExperimentalRenderSession.Current.RefreshLocalLighting();
```

较大的世界空间三维缓存存储六个方向的漫反射辐射值、环境可见度与距离矩。射线命中后读取三角形、UV、基础贴图颜色、基础颜色及自发光常量，并查询命中位置到主方向光的遮挡，计算一跳漫反射。它不会递归读取自身缓存。距离矩用于降低跨墙插值漏光，完全无效的采样退回原环境光。

较细的局部缓存存储六组半球短射线的可见度，以及主光方向的近处命中距离。表面从法线外侧的邻近单元插值，遮蔽作用于环境光、反弹光和反射；接触阴影只补主光的短距离遮挡，不把整个输出颜色乘黑。原角色 toon 分段与平涂细节保留，没有新增面部 SDF 阴影设计。

场景 URP Lit 使用运行期 `ExperimentalLocalLit` 副本，保留 URP 的法线、粗糙度、透明裁剪、附加灯、阴影和雾处理。角色直接在原 toon shader 的实验分支读取缓存。关闭局部光照或回切原方案会恢复被替换的场景材质；原资源材质不改写。GPU 从模型缓冲读取几何，支持旧 bundle 中 CPU 不可读的网格；蒙皮复用当前姿态网格。

局部光照会纳入已知 Lit/toon 表面中原来关闭投影的模型，以补回导入地图的几何缺失。Alpha Clip 用基础贴图 alpha、基础颜色 alpha 和 cutoff 过滤命中；连续透明表面仍跳过。原体积阴影使用独立实例 mask，继续沿用原不透明投影名单，不会把新增透明裁剪槽位误当作实心遮挡。

| `LocalLighting` 参数 | 作用 |
| --- | --- |
| `Enabled` / `IncludeAlphaClip` | 总开关、是否纳入透明裁剪几何 |
| `IndirectStrength` | 一跳反弹光倍率 |
| `OcclusionStrength` / `ContactShadowStrength` | 间接光遮蔽、主光接触阴影强度 |
| `ContactRadius` / `NormalBias` | 近处射线范围、表面采样偏移，单位为米 |
| `ProbeGrid` / `ProbeSpacing` | 辐射缓存单元数与间距，默认 32×16×32、0.6 米 |
| `ContactGrid` / `ContactSpacing` | 接触缓存单元数与间距，默认 80×40×80、0.1 米 |
| `RaysPerDirection` / `ContactRaysPerDirection` | 每个单元每个方向的半球射线数，默认 8 / 4 |
| `UpdatesPerSecond` / `ContactUpdatesPerSecond` | 辐射/接触缓存刷新频率，默认 2 / 4 Hz |
| `AnchorDistance` | 缓存中心距相机的前向距离，默认 6 米；`Coverage` 视图可检查范围 |
| `DebugView` | 代码控制的光照与范围诊断视图 |

缓存跟随相机并对齐到世界单元。相机移动到新的缓存范围、主光改变、参数改变、几何重建或显式刷新时更新相应缓存；否则重用上次结果。普通刷新不重复拷贝未改变的基础贴图。通过 `LocalLightingReady` / `LocalLightingStatus` 可检查是否已经产生结果。需要 DX12 / DXR；硬件不可用时跳过这部分并保留常规表面受光，绝不自动换成屏幕空间方案。

默认接触缓存每次更新约发射 640 万条射线（六组半球各 4 条，加一条主光查询），每秒更新 4 次；辐射缓存每次约 79 万条半球射线，另有命中表面的主光查询，每秒更新 2 次。需要更细接触边缘时，可设 `ContactGrid = new Vector3Int(96, 48, 96)` 与 `ContactSpacing = 0.085f`，代价是每次接触更新约 1,106 万条射线。降低 `ContactUpdatesPerSecond` 可以减少更新次数，但会加大动画遮挡滞后。

这是有限范围、有限分辨率的单次反弹实验。细小遮挡与接触边缘受单元间距影响，薄墙和复杂凹面仍可能漏光；快速动画受几何取样频率影响。基础贴图在光追中使用 64×64 副本，复杂多层材质、顶点混色、自发光贴图及点光/聚光的反弹尚未完整求解。反射仍使用前述 cubemap，没有替换为光追反射。密集几何、蒙皮重建和完整缓存刷新会增加开销，首次编译新 shader 变体也需要时间；性能数值应与冷启动编译分开比较。

## 体积管线

1. 角色和场景仍走现有导入、贴图烘焙与材质创建流程。
2. `ExperimentalRenderSession` 克隆独立的 `ExperimentalPipeline`，覆盖当前质量级别的管线引用。
3. `GeometryShadowWorld` 将有效的不透明模型子网格加入光追加速结构，包含镜头外的模型；蒙皮模型先取当前姿态。
4. 相机视锥被划分为三维体积网格。在每个网格单元中计算高度雾密度、环境散射、主方向光散射及遮挡。
5. 硬件模式使用 Unity UnifiedRayTracing 发射单元到主光源的遮挡射线。阴影贴图模式使用 URP 主光阴影。
6. 沿视线积分散射和透射率，在后处理之前合成。相机深度仅用于确定雾积分在不透明表面的位置终止。

照明和几何遮挡在三维空间计算。此方案没有新增 SSR、SSGI、屏幕空间光线追踪或 SSAO，也没有使用历史帧重投影。实验 Renderer 保留原角色描边，移除了原 Renderer 的 SSAO feature。

雾的世界空间视线通过 Unity Core 的 `ComputeClipSpacePosition` 重建，与合成阶段的深度重建采用同一 UV 约定，包含 DX12 等平台的裁剪空间 Y 翻转。体积雾独立于 Unity 普通雾开关；方向性散射仍会随观察方向变化。

实验资源放在 `Resources/StudioEditorAtmosphere`。原 `PC_RPAsset`、`PC_Renderer` 和角色材质不受配置修改；回切或销毁会话时恢复覆盖过的质量级别，并释放光追结构、姿态网格、实验反射探针与持久 GPU 资源。角色 shader 中的实验受光分支由相机作用域的全局参数控制，原方案不启用该分支。

## 参数与硬件

| 参数 | 作用 |
| --- | --- |
| `Shadows` | `HardwareRayTracing` 或 `ShadowMaps` |
| `Quality` | 控制三维网格分辨率，与屏幕输出分辨率无关 |
| `Density` / `MaxDistance` | 基础消光密度、积分终点；密度为 0 时跳过体积效果 |
| `BaseHeight` / `HeightFalloff` | 基础高度、向上指数衰减；衰减为 0 时为均匀雾 |
| `Albedo` / `Ambient` | 介质散射颜色、环境散射近似；颜色按 sRGB 转为线性值 |
| `SunScattering` / `Anisotropy` | 主光散射倍率、散射方向性 |
| `RayDistance` | 主光遮挡射线的最大距离 |
| `GeometryUpdatesPerSecond` | 几何拓扑与蒙皮姿态更新频率；模型变换每帧更新 |

16:9 相机下，Low 为 48×27×32（约 4 万条射线/帧），Balanced 为 96×54×64（约 33 万），High 为 160×90×96（约 138 万）。建议先使用 Low 检查效果。角色较多时，蒙皮取样和加速结构重建也会增加 CPU 开销；当前实现没有按可见性裁掉遮挡体。

验证环境为 Unity 6000.6.3f1、URP 17.6。Windows 硬件光追需要 Direct3D 12 和支持 DXR 的显卡。硬件后端不可用时自动使用阴影贴图，会在日志说明原因，`HardwareShadowsActive` 为 false。三维纹理或 Compute 不可用时保留原管线。

Windows Player 的 **Scripting Define Symbols** 需要包含 `INCLUDE_UNIFIED_RAYTRACING_HARDWARE_BACKEND`，这是 Unity UnifiedRayTracing 硬件后端的编译开关；本机 Standalone 设置已经加入。Editor 内可直接使用硬件后端。实验的 Editor stripper 保留 Player 所需的光追资源。本项目的 `ProjectSettings` 被 Git 忽略，其他检出环境应按这里的说明设置编译开关，并在 Windows 图形 API 中使用 Direct3D 12。

## 已知范围

- 体积效果包含雾、方向光散射和几何光追遮挡；表面新增有限范围的一跳 GI 与接触遮蔽。尚未实现光追反射、多次散射或点光/聚光的体积贡献。
- 体积光追沿用不透明投影名单。局部表面光照支持基础贴图 Alpha Clip，连续透明仍跳过；阴影贴图模式沿用原材质阴影 pass。
- 动画姿态默认 4 Hz 更新，快速运动时体积阴影可能滞后；静态/骨骼采样网格的 transform 每帧更新。
- 深度终止基于不透明表面，透明物体尚未在各自深度处独立积分。当前只服务目标 Base 相机，不支持 XR、多相机共享体积或 Scene View 预览。
- 这是实时运行期渲染资源，与导入磁盘缓存无关。加速结构不写入磁盘，不改变角色或模型缓存的索引。

## 验证

`Tests/Editor` 包含管线切换生命周期测试：反复切换、跨质量级别恢复、运行期副本释放、保留外部管线覆盖。运行期禁用释放由 GPU 集成探针验证。GPU 与实际恋活导入验证的记录位于本地 `Diagnostics/AtmosphereExperiment`；该目录是诊断产物，不属于运行期资源。

2026-10-14 在隔离项目副本中完成以下验证：

- 全部 15 项 Edit Mode 测试通过，包含 4 项实验管线测试和 11 项原有加载/缓存测试。
- RTX 3060 Laptop / Direct3D 12：镜头外挡板遮挡体积光，撤掉挡板后恢复照明，确认走硬件几何射线路径。
- Direct3D 11：同样配置自动回退阴影贴图，体积雾和主光遮挡正常。
- 均匀雾透射率实测 0.60957，理论 0.60957；表面深度截止实测 0.80213，理论 0.80252。
- 实际恋活场景的 7 个角色加载后，实验/默认方案可切换，材质 shader 保持一致；三档体积分辨率均能重新分配并渲染。

上述验证在 Editor 完成，尚未验收独立 Player。Editor 首次使用新材质变体时可能等待着色器编译，应在编译完成后比较画面。

本轮表面受光验证：18 项 Edit Mode 测试通过，包含 7 项渲染/导入材质测试和 11 项原有加载/缓存测试。GPU 验证附加点光的几何遮挡、非金属环境反射、实际恋活场景捕获、材质/灯光/相机设置恢复。实际场景的六个 cubemap 方向均有输出，共读回 98,304 个像素，平均红通道辐射值约 0.218；583 份材质中有 315 份启用了恢复后的 Lit 法线通道。前后对照位于本地 `Diagnostics/SurfaceLighting/comparison.png`。以上仍是 Editor 验证，未验收独立 Player。

本轮局部几何光照验证也在隔离副本完成：

- 更新网格缓冲初始化顺序后，18 项 Edit Mode 测试全部通过。用于命中采样的 Raw 缓冲在注册 DXR 实例之前准备，避免重新创建 GPU 缓冲影响已有实例。
- 黑色环境、画面外挡板：红墙反弹光中心读回为 `(0.401, 0.012, 0.016)`，改蓝墙后为 `(0.009, 0.012, 0.408)`；同一缓存也可被 toon 人物材质读取。关闭功能后反弹消失，原 Lit 材质恢复。
- 近处遮蔽测试中，同一地面像素的红通道从无遮蔽的 `0.877434` 降到 `0.689975`。关闭常规阴影贴图仍可得到主光接触遮挡；透明裁剪贴图的 alpha 改变也会影响几何查询。
- 实际恋活场景：7 个角色、303 个 Lit 槽位使用实验副本、432 个几何实例；回切后实验材质和持久光追资源恢复 / 释放。体积雾的透射率、表面深度终止和镜头外几何阴影回归通过。
- 前后对照位于本地 `Diagnostics/LocalLighting/comparison.png`，光照与范围诊断图也在该目录。整体质感改善仍有限，主要变化是局部遮蔽和近处色彩反弹；人物 Ramp、材质区域与高光输入需要继续适配。

性能探针使用 RTX 3060 Laptop、1024×576 单相机，包含一次 GPU 同步读回，不是完整 Game 帧率测量。复用缓存的测次约 11–14 ms，强制同时重建几何与刷新两种缓存的测次约 43–59 ms；关功能测次受其他 Editor / GPU 工作影响，不宜据此给出精确加速比。完整刷新成本明显，当前默认配置适合效果实验，尚未完成分批更新与独立 Player 性能验收。
