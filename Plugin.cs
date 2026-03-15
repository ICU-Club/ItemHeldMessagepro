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

/// <summary>
/// 插件主类，继承自TerrariaPlugin
/// </summary>
[ApiVersion(2, 1)]
public class ItemHeldMessagePlugin : TerrariaPlugin
{
    #region 插件元数据
    public override string Name => "ItemHeldMessage";
    public override string Author => "淦 & 星梦XM";
    public override string Description => "手持物品显示提示与自动执行命令，支持切换冷却防刷屏";
    public override Version Version => new(1, 1, 2);
    #endregion

    // 配置文件路径：TShock目录下的 ItemHeldMessages.json
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "ItemHeldMessages.json");
    private static PluginConfig Config = new();
    
    // 使用ConcurrentDictionary保证线程安全，替代原代码中的多个普通Dictionary
    // Key: 玩家索引(int), Value: 玩家会话状态
    private readonly ConcurrentDictionary<int, PlayerSession> _sessions = new();
    private readonly Random _random = new();

    // 主构造函数，game参数由TShock传入
    public ItemHeldMessagePlugin(Main game) : base(game) { }

    /// <summary>
    /// 插件初始化，注册钩子事件和命令
    /// </summary>
    public override void Initialize()
    {
        LoadConfig();
        
        // 注册TShock重载事件（/reload命令时触发）
        GeneralHooks.ReloadEvent += OnReload;
        
        // 注册游戏更新钩子（每帧检查手持物品）
        ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
        
        // 玩家离开服务器时清理数据（防止内存泄漏）
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        
        // 玩家进入服务器时发送欢迎消息
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
        
        // 注册聊天命令 /ihm
        Commands.ChatCommands.Add(new Command("itemheldmsg.use", MainCommand, "ihm", "手持提示")
        {
            HelpText = "手持物品提示插件，使用 /ihm help 查看帮助"
        });

        TShock.Log.Info($"[手持提示] 插件已加载 v{Version}");
    }

    /// <summary>
    /// 插件卸载，取消注册事件（防止热重载时事件重复注册）
    /// </summary>
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

    #region 配置管理

    /// <summary>
    /// 加载配置文件，如果不存在则生成默认配置
    /// </summary>
    private static void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                // 首次运行，创建默认配置
                Config = PluginConfig.CreateDefault();
                SaveConfig();
                TShock.Log.Info("[手持提示] 已生成默认配置（含切换冷却设置）");
            }
            else
            {
                // 读取现有配置
                var json = File.ReadAllText(ConfigPath);
                Config = JsonConvert.DeserializeObject<PluginConfig>(json) ?? PluginConfig.CreateDefault();
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[手持提示] 配置加载失败: {ex.Message}");
            Config = PluginConfig.CreateDefault(); // 出错时使用默认配置
        }
    }

    /// <summary>
    /// 保存配置到JSON文件
    /// </summary>
    private static void SaveConfig()
    {
        try
        {
            File.WriteAllText(ConfigPath, 
                JsonConvert.SerializeObject(Config, Formatting.Indented));
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[手持提示] 配置保存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// /reload命令回调，重新加载配置
    /// </summary>
    private void OnReload(ReloadEventArgs args)
    {
        LoadConfig();
        args.Player?.SendSuccessMessage("[手持提示] 配置已重载");
    }
    #endregion

    #region 命令系统

    /// <summary>
    /// 主命令处理器，处理 /ihm 及其子命令
    /// </summary>
    private void MainCommand(CommandArgs args)
    {
        var player = args.Player;
        var param = args.Parameters;

        // 无参数或help参数时显示帮助
        if (param.Count == 0 || param[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            ShowHelp(player);
            return;
        }

        switch (param[0].ToLower())
        {
            case "mode":
                // 模式设置：/ihm mode [0-3]
                HandleModeCommand(player, param.Count > 1 ? param[1] : null);
                break;
            case "status":
                // 查看状态
                ShowStatus(player);
                break;
            case "check":
                // 查看物品配置：/ihm check <物品ID或名称>
                if (param.Count < 2) player.SendErrorMessage("用法: /ihm check <物品ID或名称>");
                else CheckItem(player, param[1]);
                break;
            case "reload":
                // 管理员重载
                if (!player.HasPermission("itemheldmsg.admin"))
                {
                    player.SendErrorMessage("权限不足！");
                    return;
                }
                LoadConfig();
                player.SendSuccessMessage("[手持提示] 配置已重载");
                break;
            default:
                player.SendErrorMessage("未知命令，使用 /ihm help 查看帮助");
                break;
        }
    }

    /// <summary>
    /// 显示帮助信息，使用TShock的彩色文本格式 [c/RRGGBB:文本]
    /// </summary>
    private static void ShowHelp(TSPlayer player)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[c/55CDFF:★ 手持物品提示 v1.1.2 ★]");
        sb.AppendLine("[c/FFD700:/ihm] - 显示帮助");
        sb.AppendLine("[c/FFD700:/ihm mode [0-3]] - 设置显示模式 (0关 1浮动 2信息栏 3全)");
        sb.AppendLine("[c/FFD700:/ihm status] - 查看状态");
        sb.AppendLine("[c/FFD700:/ihm check <物品>] - 查看配置");
        if (player.HasPermission("itemheldmsg.admin"))
            sb.AppendLine("[c/FF6B6B:/ihm reload] - 重载配置");
        player.SendMessage(sb.ToString(), Color.Cyan);
    }

    /// <summary>
    /// 处理显示模式设置
    /// 0=关闭, 1=仅浮动文本, 2=仅信息栏, 3=全部
    /// </summary>
    private void HandleModeCommand(TSPlayer player, string? modeStr)
    {
        var session = GetSession(player.Index);
        
        // 无参数时显示当前模式
        if (string.IsNullOrEmpty(modeStr))
        {
            string[] names = { "关闭", "浮动文本", "信息栏", "全部" };
            player.SendSuccessMessage($"当前模式: {names[session.DisplayMode]} ({session.DisplayMode})");
            return;
        }

        // 解析并验证模式参数
        if (!int.TryParse(modeStr, out int mode) || mode is < 0 or > 3)
        {
            player.SendErrorMessage("无效模式！请输入 0-3");
            return;
        }

        session.DisplayMode = mode;
        string[] modeNames = { "关闭", "浮动文本", "信息栏", "全部" };
        player.SendSuccessMessage($"显示模式已切换为: {modeNames[mode]}");
    }

    /// <summary>
    /// 显示插件当前状态（全局设置+个人设置）
    /// </summary>
    private void ShowStatus(TSPlayer player)
    {
        var session = GetSession(player.Index);
        var g = Config.Global;
        
        player.SendMessage(
            $"[c/55CDFF:★ 系统状态 ★]\n" +
            $"[c/CCCCCC:浮动文本: {(g.EnableFloatText ? "开" : "关")} | 信息栏: {(g.EnableChatText ? "开" : "关")} | 命令: {(g.EnableCommand ? "开" : "关")}]\n" +
            $"[c/CCCCCC:切换冷却: {g.SwitchCooldown}秒 | 你的模式: {session.DisplayMode}]\n" +
            $"[c/CCCCCC:已配置物品: {Config.Items.Count}个]",
            Color.Cyan
        );
    }

    /// <summary>
    /// 查看指定物品的配置详情
    /// </summary>
    /// <param name="input">物品ID（数字）或物品名称（部分匹配）</param>
    private void CheckItem(TSPlayer player, string input)
    {
        int itemId;
        
        // 尝试解析为ID，否则按名称搜索
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

        // 构建配置详情字符串
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

    #region 核心逻辑：物品切换检测与冷却处理

    /// <summary>
    /// 游戏更新钩子（每帧调用）
    /// 遍历所有在线玩家，检测手持物品变化
    /// </summary>
    private void OnUpdate(EventArgs args)
    {
        // 如果全部功能都关闭，直接返回以节省性能
        if (!Config.Global.EnableFloatText && !Config.Global.EnableChatText && !Config.Global.EnableCommand)
            return;

        foreach (var player in TShock.Players)
        {
            // 跳过无效玩家：未连接、未登录
            if (player?.Active != true || !player.IsLoggedIn) continue;

            // 获取玩家当前手持物品ID（selectedItem为当前选择的物品栏索引）
            var currentItem = player.TPlayer.inventory[player.TPlayer.selectedItem].type;
            if (currentItem <= 0) continue; // 空手或无效物品

            // 获取玩家会话状态（不存在则自动创建）
            var session = GetSession(player.Index);
            
            // 检测物品是否变化（与上次记录的比较）
            if (currentItem == session.LastHeldItem) continue;

            // 切换冷却检查
            // 计算距离上次切换的时间间隔
            var timeSinceSwitch = DateTime.UtcNow - session.LastSwitchTime;
            if (timeSinceSwitch.TotalSeconds < Config.Global.SwitchCooldown)
            {
                // 在切换冷却期内：更新物品ID（避免冷却结束后错误触发），且不执行任何消息/命令
                session.LastHeldItem = currentItem;
                // 静默跳过，避免更频繁的提示
                continue;
            }

            // 通过冷却检查，正式更新切换时间和手持物品记录
            session.LastSwitchTime = DateTime.UtcNow;
            session.LastHeldItem = currentItem;

            // 检查该物品是否有配置
            if (!Config.Items.TryGetValue(currentItem.ToString(), out var itemConfig)) continue;

            // 检查玩家个人显示模式（0=完全关闭）
            if (session.DisplayMode == 0) continue;

            // 根据全局开关和玩家模式，触发相应功能
            // 模式1=浮动文本，模式2=信息栏，模式3=全部
            if (Config.Global.EnableFloatText && (session.DisplayMode == 1 || session.DisplayMode == 3))
                ProcessFloatText(player, itemConfig, session);

            if (Config.Global.EnableChatText && (session.DisplayMode == 2 || session.DisplayMode == 3))
                ProcessChatText(player, itemConfig, session);

            if (Config.Global.EnableCommand && itemConfig.Command.Enabled)
                ProcessCommand(player, itemConfig, session);
        }
    }

    /// <summary>
    /// 处理浮动文本（头顶战斗文字）
    /// 检查该消息类型的独立冷却时间
    /// </summary>
    private void ProcessFloatText(TSPlayer player, ItemDefinition config, PlayerSession session)
    {
        // 使用覆盖设置或全局设置的冷却时间
        var cooldown = config.OverrideSettings?.FloatTextCooldown ?? Config.Global.FloatTextCooldown;
        
        // 检查冷却：CanExecute会对比当前时间与上次执行时间
        if (!session.CanExecute("float", cooldown)) return;

        // 无消息配置时跳过
        if (config.FloatMessages.Count == 0) return;
        
        // 随机选择一条消息（支持多条随机显示）
        var msg = config.FloatMessages[_random.Next(config.FloatMessages.Count)];
        
        // 计算显示位置：玩家头顶 + Y轴偏移
        var yOffset = config.OverrideSettings?.YOffset ?? Config.Global.DefaultYOffset;
        var pos = player.TPlayer.Center;
        pos.Y -= yOffset;

        // 解析颜色（默认白色）
        var color = msg.Color.Length >= 3 
            ? new Color(msg.Color[0], msg.Color[1], msg.Color[2]) 
            : Color.White;

        // 发送战斗文本包给所有客户端（-1表示广播，ignoreClient:-1表示不忽略任何人）
        NetMessage.SendData(
            (int)PacketTypes.CreateCombatTextExtended,
            remoteClient: -1,
            ignoreClient: -1,
            text: NetworkText.FromLiteral(msg.Text),
            number: (int)color.PackedValue,  // 颜色打包为int
            number2: pos.X,
            number3: pos.Y
        );

        // 标记该类型已执行，记录当前时间
        session.MarkExecuted("float");
    }

    /// <summary>
    /// 处理信息栏消息（聊天栏，仅自己可见）
    /// </summary>
    private void ProcessChatText(TSPlayer player, ItemDefinition config, PlayerSession session)
    {
        var cooldown = config.OverrideSettings?.ChatTextCooldown ?? Config.Global.ChatTextCooldown;
        if (!session.CanExecute("chat", cooldown)) return;

        if (config.ChatMessages.Count == 0) return;
        var msg = config.ChatMessages[_random.Next(config.ChatMessages.Count)];

        // 将RGB转换为16进制颜色码用于TShock彩色文本 [c/RRGGBB:文本]
        var colorHex = msg.Color.Length >= 3 
            ? $"{msg.Color[0]:X2}{msg.Color[1]:X2}{msg.Color[2]:X2}" 
            : "FFFFFF";

        player.SendMessage($"[c/{colorHex}:{msg.Text}]", Color.White);
        session.MarkExecuted("chat");
    }

    /// <summary>
    /// 处理自动命令执行
    /// 包含权限检查和变量替换
    /// </summary>
    private void ProcessCommand(TSPlayer player, ItemDefinition config, PlayerSession session)
    {
        var cmd = config.Command;
        
        // 权限检查：如果配置了AllowedGroups，则检查玩家组是否在列表中
        if (!CheckPermission(player, cmd.AllowedGroups))
        {
            player.SendErrorMessage("权限不足，无法执行手持命令");
            return;
        }

        var cooldown = config.OverrideSettings?.CommandCooldown ?? Config.Global.CommandCooldown;
        if (!session.CanExecute("cmd", cooldown)) return;

        // 变量替换：将模板中的占位符替换为实际值
        var command = cmd.Cmd
            .Replace("{player}", player.Name)           // 玩家名
            .Replace("{item}", session.LastHeldItem.ToString()) // 物品ID
            .Replace("{x}", ((int)player.X).ToString()) // X坐标（整数）
            .Replace("{y}", ((int)player.Y).ToString()); // Y坐标（整数）

        try
        {
            // 以玩家身份执行命令（会走正常的权限流程）
            Commands.HandleCommand(player, command);
            session.MarkExecuted("cmd");
            TShock.Log.ConsoleDebug($"[手持提示] {player.Name} 执行: {command}");
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[手持提示] 命令执行失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查玩家权限组
    /// </summary>
    /// <param name="allowedGroups">允许执行的组列表，null或空列表表示允许所有人</param>
    private static bool CheckPermission(TSPlayer player, List<string> allowedGroups)
    {
        if (allowedGroups == null || allowedGroups.Count == 0) return true;
        // 忽略大小写比较
        return allowedGroups.Contains(player.Group.Name, StringComparer.OrdinalIgnoreCase);
    }
    #endregion

    #region 玩家事件与工具方法

    /// <summary>
    /// 玩家进入服务器欢迎消息
    /// </summary>
    private void OnGreet(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        player?.SendInfoMessage("[c/55CDFF:手持物品提示] 已加载！使用 /ihm 查看帮助");
    }

    /// <summary>
    /// 玩家离开服务器时清理内存（重要：防止长时间运行后字典膨胀）
    /// </summary>
    private void OnLeave(LeaveEventArgs args)
    {
        _sessions.TryRemove(args.Who, out _);
    }

    /// <summary>
    /// 获取或创建玩家会话
    /// ConcurrentDictionary的GetOrAdd是线程安全的
    /// </summary>
    private PlayerSession GetSession(int index)
    {
        return _sessions.GetOrAdd(index, _ => new PlayerSession());
    }
    #endregion
}

/// <summary>
/// 玩家会话类，封装单个玩家的所有运行时状态
/// 替代原代码中分散的多个Dictionary，提高代码内聚性
/// </summary>
public sealed class PlayerSession
{
    /// <summary>
    /// 上次手持的物品ID，用于检测切换
    /// </summary>
    public int LastHeldItem { get; set; } = -1;
    
    /// <summary>
    /// 显示模式：0=关闭, 1=浮动, 2=信息栏, 3=全部
    /// </summary>
    public int DisplayMode { get; set; } = 3; // 默认全部显示
    
    /// <summary>
    /// 上次切换物品的时间（UTC），用于切换冷却计算
    /// </summary>
    public DateTime LastSwitchTime { get; set; } = DateTime.MinValue;
    
    // 各消息类型的冷却记录：Key=类型("float"/"chat"/"cmd"), Value=上次执行时间
    private readonly Dictionary<string, DateTime> _cooldowns = new();

    /// <summary>
    /// 检查指定类型是否可执行（是否已过冷却时间）
    /// </summary>
    /// <param name="type">消息类型标识</param>
    /// <param name="cooldownSeconds">冷却时间（秒）</param>
    /// <returns>true表示可以执行，false表示在冷却中</returns>
    public bool CanExecute(string type, double cooldownSeconds)
    {
        if (cooldownSeconds <= 0) return true; // 冷却为0表示无限制
        if (!_cooldowns.TryGetValue(type, out var last)) return true; // 首次执行
        return (DateTime.UtcNow - last).TotalSeconds >= cooldownSeconds;
    }

    /// <summary>
    /// 标记指定类型已执行，记录当前时间
    /// </summary>
    public void MarkExecuted(string type) => _cooldowns[type] = DateTime.UtcNow;
}
