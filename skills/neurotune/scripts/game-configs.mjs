import { requireInput } from './io.mjs';

// Discovery hints, not verified local paths, supported-value tables or optimization presets.
const unrealKeys = ['bUseVSync', 'FrameRateLimit', 'ResolutionSizeX', 'ResolutionSizeY', 'FullscreenMode',
  'sg.ResolutionQuality', 'sg.ShadowQuality', 'sg.TextureQuality', 'sg.EffectsQuality', 'sg.PostProcessQuality'];
const codKeys = ['RendererWorkerCount', 'VideoMemoryScaleMP', 'CapFps', 'MaxFpsInGame', 'MaxFpsInMenu',
  'MaxFpsOutOfFocus', 'DynamicSceneResolution', 'DynamicSceneResolutionTarget', 'DLSSModeMP',
  'DLSSPerfModeMP', 'DLSSFrameGeneration', 'AMDSuperResolution2Quality', 'FSRFrameInterpolation',
  'GraphicsQuality', 'ModelQuality', 'MotionBlur'];
const games = [
  {
    id: 'cod-mw4', title: 'Call of Duty: Modern Warfare 4', editPolicy: 'verify-build-first',
    locations: [{ directory: '%LOCALAPPDATA%\\Activision\\Call of Duty\\players', files: ['s.1.0.cod26.txt'], format: 'cod-assignments' }],
    keyHints: codKeys,
    caveats: ['Third-party MW4-labelled candidate only; no first-party filename/build confirmation. Confirm the installed title and generated file before proposing edits.',
      'Preserve @-suffixed key identifiers, comments and unrelated settings. The referenced CPU-minus-one formula and full preset are not adopted.'],
    sources: ['https://italiantweaks.com/', 'https://www.pcgamingwiki.com/wiki/Call_of_Duty:_Modern_Warfare_4']
  },
  {
    id: 'cod-bo7', title: 'Call of Duty: Black Ops 7', editPolicy: 'inspect-before-proposing',
    locations: [
      { directory: '%LOCALAPPDATA%\\Activision\\Call of Duty\\players', files: ['s.1.0.cod25.txt', 'g.p.cod25.1.0.l.txt'], format: 'cod-assignments' },
      { directory: '%DOCUMENTS%\\Call of Duty\\players', files: ['s.1.0.cod25.txt', 'options.4.cod25.cst'], format: 'build-dependent' }
    ],
    keyHints: codKeys,
    caveats: ['Locations and filenames vary across migrations/builds; confirm the active file, not a backup or another CoD title. Do not synchronize all copies blindly.',
      'Key spellings/suffixes and valid ranges are build-dependent. Treat .cst as unsupported until its actual encoding/format is established.'],
    sources: ['https://github.com/bradselph/COD-Settings-Changer', 'https://www.pcgamingwiki.com/wiki/Call_of_Duty:_Black_Ops_7']
  },
  {
    id: 'battlefield-6', title: 'Battlefield 6', editPolicy: 'inspect-before-proposing',
    locations: [{ directory: '%DOCUMENTS%\\Battlefield 6\\settings', files: ['PROFSAVE_profile'], format: 'frostbite-profile' }],
    keyHints: ['GstRender.ResolutionWidth', 'GstRender.ResolutionHeight', 'GstRender.ResolutionScale',
      'GstRender.FixedResolutionScale', 'GstRender.TextureQuality', 'GstRender.ShadowQuality', 'GstRender.VSyncEnabled'],
    caveats: ['PROFSAVE_profile is typically line-oriented text, not INI. PROFSAVE is a separate potentially binary file: leave it untouched.',
      'PROFSAVE_profile has no extension. Leave it untouched unless it is plain text and the user confirms the edit; never rename it.',
      'Do not transplant older Battlefield user.cfg CPU/thread commands or assume scale units across versions.'],
    sources: ['https://www.pcgamingwiki.com/wiki/Battlefield_6']
  },
  {
    id: 'arc-raiders', title: 'ARC Raiders', editPolicy: 'inspect-before-proposing',
    locations: [
      { directory: '%LOCALAPPDATA%\\PioneerGame\\Saved\\Config\\WindowsClient', files: ['GameUserSettings.ini'], format: 'ini' },
      { directory: '%LOCALAPPDATA%\\PioneerGame\\Saved\\Config\\WinGDKClient', files: ['GameUserSettings.ini'], format: 'ini' },
      { directory: '%LOCALAPPDATA%\\PioneerGame_Entrophy\\Saved', files: ['Config\\<platform>\\GameUserSettings.ini'], format: 'ini' }
    ],
    keyHints: unrealKeys,
    caveats: ['WindowsClient/WinGDKClient and the Entrophy variant depend on distribution/build; resolve the actual platform directory.',
      'Some settings are binary SaveGames/*.sav, not editable text. Unreal scalability names are discovery hints, not proof this build honors them.'],
    sources: ['https://www.pcgamingwiki.com/wiki/ARC_Raiders']
  },
  {
    id: 'delta-force', title: 'Delta Force', editPolicy: 'in-game-only',
    locations: [
      { directory: '<game-install>\\Game\\DeltaForce\\Saved\\Config\\WindowsClient', files: ['GameUserSettings.ini', 'Engine.ini'], format: 'ini' },
      { directory: '<game-install>\\DLC\\BlackHawkDown\\BHD\\Saved\\Config\\WindowsClient', files: ['GameUserSettings.ini', 'Engine.ini'], format: 'ini' }
    ],
    keyHints: [],
    caveats: ['Developer security notice prohibits unauthorized .ini modifications, including purported performance optimizations, with possible permanent bans. Use permitted in-game graphics controls; no file rewrite proposal.',
      'Paths are for identification only; files may be absent or redirected to VirtualStore. Do not create Engine.ini or add hidden rendering overrides.'],
    sources: ['https://store.steampowered.com/news/app/2507950/view/510709111894049818', 'https://www.pcgamingwiki.com/wiki/Delta_Force']
  },
  {
    id: 'fortnite', title: 'Fortnite', editPolicy: 'inspect-before-proposing',
    locations: [{ directory: '%LOCALAPPDATA%\\FortniteGame\\Saved\\Config\\WindowsClient', files: ['GameUserSettings.ini'], format: 'ini' }],
    keyHints: [...unrealKeys, 'bUseDynamicResolution', 'sg.ViewDistanceQuality', 'sg.GlobalIlluminationQuality', 'sg.ReflectionQuality'],
    caveats: ['Rendering mode and supported upscalers depend on build/GPU; use existing supported controls, not arbitrary Engine.ini cvars.',
      'Do not force read-only: Epic documents it as a reason settings fail to save.'],
    sources: ['https://www.epicgames.com/help/fortnite-battle-royale-c-34254770/technical-support-c-38015632/what-do-i-do-if-fortnite-doesn-t-save-my-settings-a22311370',
      'https://www.pcgamingwiki.com/wiki/Fortnite']
  },
  {
    id: 'rainbow-six-siege-x', title: "Tom Clancy's Rainbow Six Siege X", editPolicy: 'inspect-before-proposing',
    locations: [{ directory: '%DOCUMENTS%\\My Games\\Rainbow Six - Siege\\<account-id>', files: ['GameSettings.ini'], format: 'ini' }],
    keyHints: ['FPSLimit', 'VSync', 'ResolutionWidth', 'ResolutionHeight', 'RefreshRate', 'WindowMode',
      'TextureQuality', 'Shadow', 'Reflection', 'AmbientOcclusion', 'AntiAliasing', 'RenderScaling'],
    caveats: ['Select the actual Ubisoft account directory. Siege X branding and renderer/key support have changed; legacy Vulkan/DX11 recipes are not current-build evidence.',
      'Verify FPSLimit semantics and quality enums in this build; lower numbers do not universally mean lower quality.'],
    sources: ['https://www.pcgamingwiki.com/wiki/Tom_Clancy%27s_Rainbow_Six_Siege']
  },
  {
    id: 'cs2', title: 'Counter-Strike 2', editPolicy: 'inspect-before-proposing',
    locations: [{ directory: '<Steam-folder>\\userdata\\<account-id>\\730\\local\\cfg',
      files: ['cs2_video.txt', 'cs2_machine_convars.vcfg', 'cs2_user_convars_0_slot0.vcfg'], format: 'source2-keyvalues' }],
    keyHints: ['setting.defaultres', 'setting.defaultresheight', 'setting.fullscreen', 'setting.mat_vsync',
      'setting.msaa_samples', 'setting.r_csgo_cmaa_enable', 'setting.r_texturefilteringquality',
      'setting.r_csgo_shadow_quality', 'fps_max', 'fps_max_ui'],
    caveats: ['Video keys live in cs2_video.txt; convars may live in separate .vcfg files and must not be inserted into the video object.',
      '.vcfg and Steam libraries outside the actual user profile are unsupported by the existing native file mechanism. Never overwrite all Steam accounts.',
      'Preserve KeyValues braces/quoting; CS:GO autoexec, -threads, -high and visibility/cheat commands are not a CS2 optimization baseline.'],
    sources: ['https://www.pcgamingwiki.com/wiki/Counter-Strike_2', 'https://developer.valvesoftware.com/wiki/Counter-Strike_2']
  },
  {
    id: 'overwatch-2', title: 'Overwatch 2', editPolicy: 'inspect-before-proposing',
    locations: [{ directory: '%DOCUMENTS%\\Overwatch\\Settings', files: ['Settings_v0.ini'], format: 'ini' }],
    keyHints: ['FrameRateCap', 'MaxFrameRate', 'RenderScale', 'DynamicRenderScale', 'VerticalSyncEnabled',
      'FullscreenWindow', 'TextureQuality', 'ShadowDetail', 'EffectsQuality', 'ModelQuality'],
    caveats: ['Inspect the existing Render section and current names/enums; renderer and dynamic render-scale controls vary by build.',
      'Account-side gameplay settings are not all stored in this file. Avoid rewriting input, account or detection fields.'],
    sources: ['https://www.pcgamingwiki.com/wiki/Overwatch_2']
  },
  {
    id: 'valorant', title: 'Valorant', editPolicy: 'inspect-before-proposing',
    locations: [{ directory: '%LOCALAPPDATA%\\VALORANT\\Saved\\Config\\<account-id>\\Windows', files: ['GameUserSettings.ini'], format: 'ini' }],
    keyHints: unrealKeys,
    caveats: ['Account directories and platform suffixes vary. Inspect the active ShooterGame settings section; do not inject generic Unreal settings absent from this build.',
      'Prefer supported in-game controls for multithreaded rendering and latency features; no Vanguard changes, hidden visibility cvars or forced unsupported resolution hacks.'],
    sources: ['https://www.pcgamingwiki.com/wiki/Valorant']
  },
  {
    id: 'apex-legends', title: 'Apex Legends', editPolicy: 'inspect-before-proposing',
    locations: [
      { directory: '%SAVEDGAMES%\\Respawn\\Apex\\local', files: ['videoconfig.txt', 'settings.cfg'], format: 'source-keyvalues-or-convars' },
      { directory: '%SAVEDGAMES%\\Respawn\\Apex\\profile', files: ['profile.cfg'], format: 'convars' }
    ],
    keyHints: ['setting.defaultres', 'setting.defaultresheight', 'setting.fullscreen', 'setting.mat_vsync_mode',
      'setting.stream_memory', 'setting.r_lod_switch_scale', 'setting.csm_enabled', 'setting.ssao_enabled', 'setting.dvs_enable'],
    caveats: ['Video keys belong to videoconfig.txt, not every listed .cfg. Preserve input/profile settings.',
      'Texture streaming budget units/limits need current-build and measured VRAM confirmation. Do not transplant removed Source cvars or visibility-removal autoexecs.'],
    sources: ['https://www.pcgamingwiki.com/wiki/Apex_Legends']
  },
  {
    id: 'escape-from-tarkov', title: 'Escape from Tarkov', editPolicy: 'inspect-before-proposing',
    locations: [
      { directory: '%APPDATA%\\Battlestate Games\\Escape from Tarkov\\Settings', files: ['Graphics.ini', 'Game.ini'], format: 'json-in-ini' },
      { directory: '%DOCUMENTS%\\Escape from Tarkov', files: ['local.ini', 'shared.ini'], format: 'legacy-build-dependent' }
    ],
    keyHints: ['Resolution', 'FullScreenMode', 'VSync', 'TexturesQuality', 'ShadowsQuality', 'ObjectLodQuality',
      'OverallVisibility', 'SuperSampling', 'AntiAliasing', 'DlssMode', 'Fsr2Mode', 'OnlyPhysicalCores', 'AutoEmptyWorkingSet'],
    caveats: ['Modern .ini files may contain JSON objects: inspect actual bytes before choosing a parser. Legacy Documents paths are not the current default.',
      'Physical-core and RAM-cleaner options are experiment candidates, not universal fixes; preserve game/account/input data and test the same map/workload.',
      'SPT/mod configuration is not evidence for the live multiplayer build.'],
    sources: ['https://www.pcgamingwiki.com/wiki/Escape_from_Tarkov']
  },
  {
    id: 'pubg', title: 'PUBG: Battlegrounds', editPolicy: 'inspect-before-proposing',
    locations: [{ directory: '%LOCALAPPDATA%\\TslGame\\Saved\\Config\\WindowsNoEditor', files: ['GameUserSettings.ini'], format: 'ini' }],
    keyHints: [...unrealKeys, 'ScreenScale', 'sg.ViewDistanceQuality', 'sg.FoliageQuality'],
    caveats: ['Use the existing TslGame settings/scalability sections and current supported controls. Historical Engine.ini grass/smoke, network or anti-cheat tweaks are not permitted optimization guidance.',
      'ScreenScale and sg.ResolutionQuality may use different units/precedence; never change both blindly.'],
    sources: ['https://www.pcgamingwiki.com/wiki/PUBG:_Battlegrounds']
  }
];

export function gameConfigReferences(game) {
  const selected = game === 'all' ? games : games.filter(entry => entry.id === game);
  requireInput(selected.length > 0, 'Unknown game ID. Use game-configs --game all to list supported references.');
  return { schemaVersion: 1, kind: 'game-config-reference', systemWrites: false, filesRead: false,
    presetsProvided: false, reference: 'references/game-configs.md', games: structuredClone(selected) };
}
