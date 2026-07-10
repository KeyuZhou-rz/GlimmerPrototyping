# Demo2 对齐 · 地形精细化（配色落地 + 巨石/碎石/草簇散布）

> 参考图:`Assets/demo2.png`(低模 diorama:刻面巨石群、密草地毯、碎石零星、硬刻面)
> 硬约束:**维持草原化配色**(旱季稻草黄/骨沙,非 demo2 的饱和浓绿+蓝灰岩);**不动 skybox、不动水体、不动雾/日光**;**不改 TerrainGenerator.cs——保持地形现有粗粝形体**(用户 2026-07-06 决策);只做地形材质 + 地形相关模型。
> 与 `Docs/DemoVisualAlignment.md` 的关系:本轮落地其 **B1 调色板**(用户已确认)+ 原「下一轮」中的巨石/草簇;A 节(天空/雾/日光)与 D 节(水面)继续推迟。

## Context

上一轮已落地 Glimmer 统一渲染体系(GlimmerToonCore + Terrain/Toon/RainStreak 三 shader、幂等 Setup 菜单),但画面精细度与参考图差距大。根因:demo2 的精细感来自**密集地面覆盖物**(草簇地毯、碎石)+ **刻面巨石体量**,而当前场景地面是空的(仅 6 棵手摆树),平原 `detailAmplitude=0` 完全死平,地形材质还是旧的偏橄榄绿配色(B1 草原调色板定稿过但从未写入磁盘)。

项目现状(已核实):**无任何岩石资产/代码**,全部新建;`GrassSystem.cs`(GPU instancing)存在但无材质资产、无地形过滤、只在 Play 模式渲染;`Glimmer/Toon` shader **不支持 instancing**(无 `multi_compile_instancing`),不能直接用于 DrawMeshInstanced。

## 改动清单(按执行顺序)

### 1. ~~地形微起伏:Terrace 重排序~~ — **已取消**(用户决策:不修改 TerrainGenerator,保持地形现有的粗粝感)

原方案(台地只作用宏观形体、detail/河道叠其上、detailAmplitude 0→0.18)整体废弃。`TerrainGenerator.cs` 与场景地形参数(detailAmplitude=0、useTerrace=1、terraceStep=1.36 等)一律不动;地面近景的精细感全部由步骤 3-6 的巨石/碎石/草簇承载。

### 2. B1 草原调色板落地 — MODIFY `Assets/Editor/GlimmerVisualSetup.cs` `SetupTerrain()` (92-106 行)(材质 .mat 由其幂等写入)

| 属性 | 现值 | 新值 |
|---|---|---|
| _SandColor | (0.72,0.64,0.46) | (0.80,0.73,0.58) 苍白骨沙 |
| _LowlandColor | (0.40,0.50,0.27) 绿 | (0.66,0.63,0.44) 干稻草 |
| _PlainsColor | (0.48,0.53,0.27) 绿 | (0.72,0.68,0.50) 苍白干草 |
| _HighlandColor | (0.56,0.53,0.31) | (0.70,0.63,0.47) 日晒褪色 |
| _PeakColor | (0.50,0.46,0.42) | (0.55,0.51,0.45) **暖灰棕**(非冷板岩,避蓝灰) |
| _CliffColor | (0.42,0.36,0.30) | (0.38,0.34,0.29) 暖深棕 |
| _ShadowTint | (0.30,0.38,0.46) | (0.34,0.40,0.50) |

其余:`_AmbientBoost` 0.9→0.95、`_BandSoftness` 1.3→1.4、`_CliffStart` 0.45→0.42、`_CliffSharp` 0.18→0.16。**不写任何 TerrainGenerator 字段**,仅反射调 `Generate()` 刷新材质高度带。

**交叉项**:`SetupPostFX()` 的 ColorAdjustments `saturation +8`(~217 行)→ **0**(+8 会把苍白稻草推回绿黄,顶撞新调色板;原计划 E6 已有此校准条目)。

### 3. 程序化巨石 — CREATE `Assets/GlimmerDiary/Scripts/Core/RockMeshBuilder.cs`

- 种子化 cube-sphere → 逐顶点径向噪声位移 → 非均匀轴缩放(x,z∈[0.8,1.3], y∈[0.6,1.0] 偏扁)→ 底部压平 → **炸开为非共享顶点 + RecalculateNormals**(硬刻面,同 TerrainGenerator 模式)。
- 编辑期烘焙**复用池**(非逐实例):L×6 @300-600 tris、M×8 @150-300、S×6 @40-120。`AssetDatabase.CreateAsset` 存 mesh 到 `Assets/Models/Rocks/`,`PrefabUtility.SaveAsPrefabAsset` 存 prefab 到 `Assets/Prefabs/Rocks/`(mesh 必须先持久化否则 prefab 引用悬空)。
- 材质:`Glimmer/Toon` 无贴图可直接用(_BaseMap 默认 white,纯 _BaseColor 着色,无 UV 要求)。建 **3 个材质资产**轮换(不用 MPB——MPB 会破坏静态合批):Rock_Glimmer_A (0.55,0.51,0.45) / B (0.48,0.44,0.38) / C (0.42,0.38,0.32),`_ShadowTint` 同地形 (0.34,0.40,0.50)。实例标记 BatchingStatic → 全场 3 个静态批次。

### 4. 岩石散布 — CREATE `Assets/Editor/GlimmerTerrainDecor.cs`(菜单 `Tools/Glimmer/Scatter Terrain Decor`)

- 确定性种子、幂等(删旧父节点 `TerrainDecor` 重建);**直接 `MeshCollider.Raycast`** 地形碰撞体(不用 Physics.Raycast,避免打中树/水)。
- 分区镜像 TreePlacement 的采样思路(**不改 TreePlacement.cs**,其 ~20 行 DistanceToWater 数学复制一份并注释来源):
  - 崖缝带(b1=0.35 / b2=0.85 区域边界):L/M 簇 2-4 块
  - 山环(外圈 ~20%):L/M
  - 河岸:S 碎石带
  - 平原:稀疏 L 孤石
- 数量:L 30 / M 90 / S 280;下沉 15-30%、随机 yaw、缩放抖动 0.8-1.3;河道排除。

### 5. 草簇 shader — CREATE `Assets/Shaders/GlimmerGrass.shader`(Glimmer/Toon 分叉)

参考图式实心几何草簇(无 alpha 贴图)。关键差异:
- **instancing 三件套**(DrawMeshInstanced 必需):`#pragma multi_compile_instancing` + Attributes 加 `UNITY_VERTEX_INPUT_INSTANCE_ID` + vert 首行 `UNITY_SETUP_INSTANCE_ID(IN)`。**不需要** UNITY_INSTANCING_BUFFER——无逐实例材质属性;`_WindDirection/_WindStrength/_WindFrequency`(沿用 GrassSystem 已设置的 MPB 属性名)作普通 CBUFFER uniform 即可。DrawMeshInstanced 走 instancing 路径而非 SRP Batcher,MPB 生效。
- 根→尖渐变 `lerp(_RootColor,_TipColor, uv.y)`;风摆按 `uv.y²` 加权(根不动);`Cull Off`;GlimmerToonCore 光照 + MixFog;**去掉 ShadowCaster UsePass**(不投影,URP Lit 的 pass 也不会摆动)。
- 配套 CREATE `Assets/Materials/Glimmer/Grass_Glimmer.mat`:root (0.62,0.60,0.40) / tip (0.80,0.76,0.55) 干草黄。

### 6. GrassSystem 改造 — MODIFY `Assets/Scripts/Lsystemv2/GrassSystem.cs`

- 新增 `CreateTuftMesh()`:3 片交叉锥形 quad(~6-10 tris),宽根尖顶,法线偏 +Y 防背面死黑;替换单叶片默认 mesh。
- **渲染改挂 `RenderPipelineManager.beginCameraRendering`**(替代 Update 里的 Draw)+ `[ExecuteAlways]`:SceneView、编辑态截图的临时相机 `cam.Render()`、Play 全路径都触发(URP 下 cam.Render() 会触发该回调;Update 方式在截图相机上不可靠)。`DrawMeshInstanced(..., camera: cam)` 限定相机。OnEnable/OnDisable 挂卸回调,null-guard _propertyBlock。
- 采样过滤(现在是无过滤全图撒):序列化 `MeshCollider terrainCollider` 引用只打地形;坡度 ≤25°;`y > waterY + 0.2`(waterY 由 Setup 从水面 renderer.bounds 读入,≈0);河道排除(复制 DistanceToWater 数学);高度带密度:沙滩/崖/山 0、低地 0.8、平原 1.2、高地 0.3 簇/m²;`ValueNoise(xz*0.045)` 簇团门控——**与地形 shader 草甸色斑同频**,草簇聚落对齐地面色斑。
- `grassDensity=100` 语义修正(现值 = 2.56M 叶片)→ 按上述密度重解释,160×160 全图约 **~30k 簇 ≈ 30 批/相机**,castShadows=false。
- `SetEmotionState` 保留但**不接线**(Layer-3 纪律:将来由 WorldAtmosphereBinder 式绑定器从 E_env 驱动,本轮不碰)。

### 7. Setup 集成 — MODIFY `Assets/Editor/GlimmerVisualSetup.cs`

- 新菜单 `Tools/Glimmer/Setup Grass`:幂等建/更新 Grass_Glimmer.mat(勾 enableInstancing)、烘焙 `Assets/Models/Grass/GrassTuft.asset`、find-or-create GrassSystem 节点、接线(材质/mesh/terrainCollider/waterY)、GenerateGrass()。
- `Run()` 末尾追加两个新 Setup(保持幂等);**顺序强制**:地形 Generate(重建 collider)→ Scatter Decor → Setup Grass。

### 8. 验证闭环

1. `Tools/Glimmer/Setup Visual Style` → `read_console` 查编译/运行错误。
2. `Scatter Terrain Decor` → `Setup Grass`。
3. 编辑态:`Preview Golden Hour` → ClaudeViewCapture 截 overview + maincam(草经 beginCameraRendering 出现在截图里)。
4. 运行态:`Playtest Clear Capture`(Simulate 快进模式)。
5. 对照 demo2.png 五检:巨石簇读作刻面体量 / 草毯密度成立 / 地面苍白稻草**非绿** / 岩石暖棕**非蓝灰** / **天空与水完全未变**。
6. 性能:草 ~30 批/相机、岩石 3 静态批;只迭代数据(数量/密度/颜色)直到静帧成立。
7. 分模块提交:`feat(terrain)` 调色板+微起伏 / `feat(rocks)` / `feat(grass)`。

## 风险

- **[中] 草在截图临时相机不渲染**:beginCameraRendering 方案专治;若个别路径仍缺,回退为截图工具临时 SendMessage 触发。
- **[中] PostFX 饱和度清零影响全场景观感**(树/水也在全局 Volume 下):树用贴图色受影响小;截图对比校准,可折中 -4~0。
- **[中] 地形保持死平(detailAmplitude=0 不动)后,巨石/草簇与地面的衔接生硬**:靠下沉深度(15-30%)和 S 碎石过渡带缓解;若近景仍空洞,只加覆盖物密度,不回头改地形。
- **[低] 30k 草簇 CPU 提交开销**(Matrix4x4 数组常驻,无逐帧 GC):不行就降密度或砍高地带。

## 明确不动

skybox(sky.mat)、水体(StylizedWater.mat + Water mesh)、雾/日光/天气控制器数值、**`TerrainGenerator.cs` 及场景地形参数(保持粗粝形体)**、`TreePlacement.cs`(休眠的树木散布器,将来情绪门控用)、手摆 BrokenVector 树。
