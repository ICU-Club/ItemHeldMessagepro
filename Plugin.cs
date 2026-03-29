using System.Collections.Concurrent;
using System.Text;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace ItemHeldMessage;

[ApiVersion(2, 1)]
public class ItemHeldMessagePlugin : TerrariaPlugin
{
    #region 插件元数据
    public override string Name => "ItemHeldMessage";
    public override string Author => "淦 & 星梦XM";
    public override string Description => "手持物品显示提示与自动执行命令，支持切换冷却防刷屏";
    public override Version Version => new(1, 1, 3);
    #endregion

    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "ItemHeldMessages.json");
    private static PluginConfig Config = new();
    
    private readonly ConcurrentDictionary<int, PlayerSession> _sessions = new();
    private readonly Random _random = new();

    public ItemHeldMessagePlugin(Main game) : base(game) { }

    public override void Initialize()
    {
        // 初始化时加载，失败则使用默认配置
        if (!LoadConfig())
        {
            TShock.Log.Warn("[手持提示] 初始化时配置加载失败，使用内存默认配置。请检查目录权限或手动创建配置文件。");
        }
        
        GeneralHooks.ReloadEvent += OnReload;
        ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
        
        Commands.ChatCommands.Add(new Command("itemheldmsg.use", MainCommand, "ihm", "手持提示")
        {
            HelpText = "手持物品提示插件，使用 /ihm help 查看帮助"
        });

        TShock.Log.Info($"[手持提示] 插件已加载 v{Version}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GeneralHooks.ReloadEvent -= OnReload;
            ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
        }
        base.Dispose(disposing);
    }

    #region 配置管理（已修复）

    /// <summary>
    /// 加载配置文件，如果不存在则生成默认配置
    /// 返回：true=成功（加载现有配置或成功创建新配置），false=失败（文件不存在且创建失败，或加载出错）
    /// </summary>
    private static bool LoadConfig()
    {
        try
        {
            // 确保目录存在
            var configDir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                try
                {
                    Directory.CreateDirectory(configDir);
                    TShock.Log.Info($"[手持提示] 已创建配置目录: {configDir}");
                }
                catch (Exception ex)
                {
                    TShock.Log.Error($"[手持提示] 创建配置目录失败: {ex.Message}");
                    Config = PluginConfig.CreateDefault();
                    return false;
                }
            }

            if (!File.Exists(ConfigPath))
            {
                // 首次运行，创建默认配置
                Config = PluginConfig.CreateDefault();
                TShock.Log.Info("[手持提示] 配置文件不存在，正在生成默认配置...");
                
                if (SaveConfig())
                {
                    TShock.Log.Info($"[手持提示] 默认配置文件已生成: {ConfigPath}");
                    return true;
                }
                else
                {
                    TShock.Log.Error("[手持提示] 无法生成默认配置文件，请检查写入权限");
                    return false;
                }
            }
            else
            {
                // 读取现有配置
                var json = File.ReadAllText(ConfigPath);
                var loadedConfig = JsonConvert.DeserializeObject<PluginConfig>(json);
                
                if (loadedConfig != null)
                {
                    Config = loadedConfig;
                    TShock.Log.Info("[手持提示] 配置文件加载成功");
                    return true;
                }
                else
                {
                    TShock.Log.Warn("[手持提示] 配置文件解析为空，使用默认配置（不自动覆盖原文件）");
                    Config = PluginConfig.CreateDefault();
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[手持提示] 配置加载异常: {ex.Message}");
            Config = PluginConfig.CreateDefault();
            return false;
        }
    }

    /// <summary>
    /// 保存配置到JSON文件
    /// 返回：true=成功，false=失败
    /// </summary>
    private static bool SaveConfig()
    {
        try
        {
            // 双重检查目录（防删除情况）
            var configDir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            
            var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
            TShock.Log.Info($"[手持提示] 配置文件已保存: {ConfigPath}");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            TShock.Log.Error($"[手持提示] 配置保存失败（权限不足）: {ex.Message}");
            TShock.Log.Error($"[手持提示] 请检查服务器对目录 '{Path.GetDirectoryName(ConfigPath)}' 的写入权限");
            return false;
        }
        catch (IOException ex)
        {
            TShock.Log.Error($"[手持提示] 配置保存失败（IO错误）: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[手持提示] 配置保存失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// /reload命令回调（已修复）
    /// 修复：添加权限检查，处理配置生成失败的情况
    /// </summary>
    private void OnReload(ReloadEventArgs args)
    {
        // 权限检查
        if (args.Player != null && !args.Player.HasPermission("itemheldmsg.admin"))
        {
            args.Player.SendErrorMessage("[手持提示] 权限不足！你需要 itemheldmsg.admin 权限");
            return;
        }
        
        bool fileExistedBefore = File.Exists(ConfigPath);
        bool success = LoadConfig();
        
        if (success)
        {
            if (!fileExistedBefore)
            {
                string msg = $"[手持提示] 配置已生成并加载: {ConfigPath}";
                args.Player?.SendSuccessMessage(msg);
                TShock.Log.Info(msg);
            }
            else
            {
                string msg = "[手持提示] 配置已重载";
                args.Player?.SendSuccessMessage(msg);
                TShock.Log.Info(msg);
            }
        }
        else
        {
            string msg = "[手持提示] 配置操作失败！请检查后台错误日志和目录权限";
            args.Player?.SendErrorMessage(msg);
            TShock.Log.Error(msg);
            
            if (!fileExistedBefore)
            {
                TShock.Log.Error($"[手持提示] 预期配置文件路径: {Path.GetFullPath(ConfigPath)}");
            }
        }
    }
    #endregion

    #region 命令系统（保持不变）

    private void MainCommand(CommandArgs args)
    {
        var player = args.Player;
        var param = args.Parameters;

        if (param.Count == 0 || param[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            ShowHelp(player);
            return;
        }

        switch (param[0].ToLower())
        {
            case "mode":
                HandleModeCommand(player, param.Count > 1 ? param[1] : null);
                break;
            case "status":
                ShowStatus(player);
                break;
            case "check":
                if (param.Count < 2) player.SendErrorMessage("用法: /ihm check <物品ID或名称>");
                else CheckItem(player, param[1]);
                break;
            case "reload":
                if (!player.HasPermission("itemheldmsg.admin"))
                {
                    player.SendErrorMessage("权限不足！需要 itemheldmsg.admin 权限");
                    return;
                }
                
                bool fileExisted = File.Exists(ConfigPath);
                if (LoadConfig())
                {
                    if (!fileExisted)
                        player.SendSuccessMessage("[手持提示] 配置已生成并加载");
                    else
                        player.SendSuccessMessage("[手持提示] 配置已重载");
                }
                else
                {
                    player.SendErrorMessage("[手持提示] 配置重载失败，请检查后台日志");
                }
                break;
            default:
                player.SendErrorMessage("未知命令，使用 /ihm help 查看帮助");
                break;
        }
    }

    private static void ShowHelp(TSPlayer player)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[c/55CDFF:★ 手持物品提示 v1.1.3 ★]");
        sb.AppendLine("[c/FFD700:/ihm] - 显示帮助");
        sb.AppendLine("[c/FFD700:/ihm mode [0-3]] - 设置显示模式 (0关 1浮动 2信息栏 3全)");
        sb.AppendLine("[c/FFD700:/ihm status] - 查看状态");
        sb.AppendLine("[c/FFD700:/ihm check <物品>] - 查看配置");
        if (player.HasPermission("itemheldmsg.admin"))
        {
            sb.AppendLine("[c/FF6B6B:/ihm reload] - 重载配置");
            sb.AppendLine("[c/FF6B6B:/reload] - 也可重载本插件配置（需权限）");
        }
        player.SendMessage(sb.ToString(), Color.Cyan);
    }

    private void HandleModeCommand(TSPlayer player, string? modeStr)
    {
        var session = GetSession(player.Index);
        
        if (string.IsNullOrEmpty(modeStr))
        {
            string[] names = { "关闭", "浮动文本", "信息栏", "全部" };
            player.SendSuccessMessage($"当前模式: {names[session.DisplayMode]} ({session.DisplayMode})");
            return;
        }

        if (!int.TryParse(modeStr, out int mode) || mode is < 0 or > 3)
        {
            player.SendErrorMessage("无效模式！请输入 0-3");
            return;
        }

        session.DisplayMode = mode;
        string[] modeNames = { "关闭", "浮动文本", "信息栏", "全部" };
        player.SendSuccessMessage($"显示模式已切换为: {modeNames[mode]}");
    }

    private void ShowStatus(TSPlayer player)
    {
        var session = GetSession(player.Index);
        var g = Config.Global;
        
        player.SendMessage(
            $"[c/55CDFF:★ 系统状态 ★]\n" +
            $"[c/CCCCCC:浮动文本: {(g.EnableFloatText ? "开" : "关")} | 信息栏: {(g.EnableChatText ? "开" : "关")} | 命令: {(g.EnableCommand ? "开" : "关")}]\n" +
            $"[c/CCCCCC:切换冷却: {g.SwitchCooldown}秒 | 你的模式: {session.DisplayMode}]\n" +
            $"[c/CCCCCC:已配置物品: {Config.Items.Count}个]\n" +
            $"[c/CCCCCC:配置文件: {(File.Exists(ConfigPath) ? "存在" : "[c/FF6B6B:缺失]")}]",
            Color.Cyan
        );
    }

    private void CheckItem(TSPlayer player, string input)
    {
        int itemId;
        
        if (!int.TryParse(input, out itemId))
        {
            var items = TShock.Utils.GetItemByName(input);
            if (items.Count == 0) { player.SendErrorMessage($"未找到: {input}"); return; }
            if (items.Count > 1) { player.SendMultipleMatchError(items.Select(i => $"{i.Name}({i.type})")); return; }
            itemId = items[0].type;
        }

        var name = Lang.GetItemNameValue(itemId) ?? $"物品{itemId}";
        if (!Config.Items.TryGetValue(itemId.ToString(), out var def))
        {
            player.SendMessage($"[c/FF6B6B:{name} (ID:{itemId})] 未配置", Color.White);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[c/55CDFF:★ {name} (ID:{itemId}) ★]");
        
        if (def.FloatMessages.Count > 0)
        {
            sb.AppendLine($"[c/00FF00:浮动消息 ({def.FloatMessages.Count}条):]");
            def.FloatMessages.ForEach(m => sb.AppendLine($"  [c/FFD700:] {m.Text}"));
        }
        
        if (def.ChatMessages.Count > 0)
        {
            sb.AppendLine($"[c/00FF00:信息栏消息 ({def.ChatMessages.Count}条):]");
            def.ChatMessages.ForEach(m => sb.AppendLine($"  [c/FFD700:] {m.Text}"));
        }

        if (def.Command.Enabled)
        {
            sb.AppendLine($"[c/00FF00:自动命令:] {def.Command.Cmd}");
            sb.AppendLine($"  [c/CCCCCC:权限组: {string.Join(", ", def.Command.AllowedGroups)}]");
        }

        player.SendMessage(sb.ToString(), Color.Cyan);
    }
    #endregion

    #region 核心逻辑（保持不变）

    private void OnUpdate(EventArgs args)
    {
        if (!Config.Global.EnableFloatText && !Config.Global.EnableChatText && !Config.Global.EnableCommand)
            return;

        foreach (var player in TShock.Players)
        {
            if (player?.Active != true || !player.IsLoggedIn) continue;

            var currentItem = player.TPlayer.inventory[player.TPlayer.selectedItem].type;
            if (currentItem <= 0) continue;

            var session = GetSession(player.Index);
            
            if (currentItem == session.LastHeldItem) continue;

            // 切换冷却检查
            var timeSinceSwitch = DateTime.UtcNow - session.LastSwitchTime;
            if (timeSinceSwitch.TotalSeconds < Config.Global.SwitchCooldown)
            {
                session.LastHeldItem = currentItem;
                continue;
            }

            session.LastSwitchTime = DateTime.UtcNow;
            session.LastHeldItem = currentItem;

            if (!Config.Items.TryGetValue(currentItem.ToString(), out var itemConfig)) continue;
            if (session.DisplayMode == 0) continue;

            if (Config.Global.EnableFloatText && (session.DisplayMode == 1 || session.DisplayMode == 3))
                ProcessFloatText(player, itemConfig, session);

            if (Config.Global.EnableChatText && (session.DisplayMode == 2 || session.DisplayMode == 3))
                ProcessChatText(player, itemConfig, session);

            if (Config.Global.EnableCommand && itemConfig.Command.Enabled)
                ProcessCommand(player, itemConfig, session);
        }
    }

    private void ProcessFloatText(TSPlayer player, ItemDefinition config, PlayerSession session)
    {
        var cooldown = config.OverrideSettings?.FloatTextCooldown ?? Config.Global.FloatTextCooldown;
        
        if (!session.CanExecute("float", cooldown)) return;
        if (config.FloatMessages.Count == 0) return;
        
        var msg = config.FloatMessages[_random.Next(config.FloatMessages.Count)];
        var yOffset = config.OverrideSettings?.YOffset ?? Config.Global.DefaultYOffset;
        var pos = player.TPlayer.Center;
        pos.Y -= yOffset;

        var color = msg.Color.Length >= 3 
            ? new Color(msg.Color[0], msg.Color[1], msg.Color[2]) 
            : Color.White;

        NetMessage.SendData(
            (int)PacketTypes.CreateCombatTextExtended,
            remoteClient: -1,
            ignoreClient: -1,
            text: NetworkText.FromLiteral(msg.Text),
            number: (int)color.PackedValue,
            number2: pos.X,
            number3: pos.Y
        );

        session.MarkExecuted("float");
    }

    private void ProcessChatText(TSPlayer player, ItemDefinition config, PlayerSession session)
    {
        var cooldown = config.OverrideSettings?.ChatTextCooldown ?? Config.Global.ChatTextCooldown;
        if (!session.CanExecute("chat", cooldown)) return;

        if (config.ChatMessages.Count == 0) return;
        var msg = config.ChatMessages[_random.Next(config.ChatMessages.Count)];

        var colorHex = msg.Color.Length >= 3 
            ? $"{msg.Color[0]:X2}{msg.Color[1]:X2}{msg.Color[2]:X2}" 
            : "FFFFFF";

        player.SendMessage($"[c/{colorHex}:{msg.Text}]", Color.White);
        session.MarkExecuted("chat");
    }

    private void ProcessCommand(TSPlayer player, ItemDefinition config, PlayerSession session)
    {
        var cmd = config.Command;
        
        if (!CheckPermission(player, cmd.AllowedGroups))
        {
            player.SendErrorMessage("权限不足，无法执行手持命令");
            return;
        }

        var cooldown = config.OverrideSettings?.CommandCooldown ?? Config.Global.CommandCooldown;
        if (!session.CanExecute("cmd", cooldown)) return;

        var command = cmd.Cmd
            .Replace("{player}", player.Name)
            .Replace("{item}", session.LastHeldItem.ToString())
            .Replace("{x}", ((int)player.X).ToString())
            .Replace("{y}", ((int)player.Y).ToString());

        try
        {
            Commands.HandleCommand(player, command);
            session.MarkExecuted("cmd");
            TShock.Log.Info($"[手持提示] {player.Name} 执行: {command}");
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[手持提示] 命令执行失败: {ex.Message}");
        }
    }

    private static bool CheckPermission(TSPlayer player, List<string> allowedGroups)
    {
        if (allowedGroups == null || allowedGroups.Count == 0) return true;
        return allowedGroups.Contains(player.Group.Name, StringComparer.OrdinalIgnoreCase);
    }
    #endregion

    #region 玩家事件与工具方法（保持不变）

    private void OnGreet(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        player?.SendInfoMessage("[c/55CDFF:手持物品提示] 已加载！使用 /ihm 查看帮助");
    }

    private void OnLeave(LeaveEventArgs args)
    {
        _sessions.TryRemove(args.Who, out _);
    }

    private PlayerSession GetSession(int index)
    {
        return _sessions.GetOrAdd(index, _ => new PlayerSession());
    }
    #endregion
}

public sealed class PlayerSession
{
    public int LastHeldItem { get; set; } = -1;
    public int DisplayMode { get; set; } = 3;
    public DateTime LastSwitchTime { get; set; } = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _cooldowns = new();

    public bool CanExecute(string type, double cooldownSeconds)
    {
        if (cooldownSeconds <= 0) return true;
        if (!_cooldowns.TryGetValue(type, out var last)) return true;
        return (DateTime.UtcNow - last).TotalSeconds >= cooldownSeconds;
    }

    public void MarkExecuted(string type) => _cooldowns[type] = DateTime.UtcNow;
}
