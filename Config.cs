using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ItemHeldMessage;

/// <summary>
/// 插件主配置类，对应配置文件 ItemHeldMessages.json
/// </summary>
public sealed class PluginConfig
{
    /// <summary>
    /// 全局设置，适用于所有物品的默认行为
    /// </summary>
    [JsonProperty("全局设置", Order = 1)]
    public GlobalSettings Global { get; set; } = new();

    /// <summary>
    /// 物品特定配置，Key为物品ID（字符串），Value为该物品的配置
    /// </summary>
    [JsonProperty("物品配置", Order = 2)]
    public Dictionary<string, ItemDefinition> Items { get; set; } = new();

    /// <summary>
    /// 创建默认配置，首次生成配置文件时调用
    /// 包含示例物品：生命水晶(ID:29)、铂金币(ID:74)
    /// </summary>
    public static PluginConfig CreateDefault() => new()
    {
        Global = new GlobalSettings
        {
            // 启用所有功能
            EnableFloatText = true,
            EnableChatText = true,
            EnableCommand = true,
            
            // ★ 核心防刷屏设置：切换武器后的强制冷却（秒）
            // 无论之前冷却是否结束，切换物品后必须等待此时间才能触发
            SwitchCooldown = 1.5,
            
            // 各类消息的独立冷却时间
            FloatTextCooldown = 3.0,
            ChatTextCooldown = 2.0,
            CommandCooldown = 5.0,
            
            // 浮动文本默认高度偏移（像素）
            DefaultYOffset = 50f,
            
            // 冷却提示模板，{type}和{seconds}为占位符
            CooldownMessage = "该{type}冷却中，请在{seconds:F1}秒后再试！",
            SwitchCooldownMessage = "切换过于频繁，请等待{seconds:F1}秒"
        },
        Items = new()
        {
            // 生命水晶 (ID: 29) 示例配置
            ["29"] = new ItemDefinition
            {
                Name = "生命水晶",
                FloatMessages =
                [
                    new MessageData { Text = "关住星梦喵，关住星梦谢谢喵！", Color = [0, 100, 255] },
                    new MessageData { Text = "生命水晶: 右键使用增加20点生命上限", Color = [0, 200, 100] }
                ],
                ChatMessages =
                [
                    new MessageData { Text = "你将生命水晶贴近了你的耳朵，你听到了微弱的声音", Color = [0, 255, 150] },
                    new MessageData { Text = "关住星梦喵，关住星梦谢谢喵！", Color = [0, 255, 150] }
                ],
                Command = new CommandSettings
                {
                    Enabled = true,
                    Cmd = "/heal 20",
                    AllowedGroups = ["admin", "vip"],
                    Description = "恢复20点生命值"
                },
                // 覆盖全局设置：该物品的独立冷却时间
                OverrideSettings = new()
                {
                    YOffset = 60,
                    FloatTextCooldown = 5,
                    ChatTextCooldown = 3
                }
            },
            // 铂金币 (ID: 74) 示例配置
            ["74"] = new ItemDefinition
            {
                Name = "铂金币",
                FloatMessages = [new() { Text = "铂金币: 价值连城！", Color = [255, 215, 0] }],
                ChatMessages = [new() { Text = "价值可是高达100个金币呢（）", Color = [255, 223, 0] }],
                Command = new CommandSettings
                {
                    Enabled = true,
                    Cmd = "/give {player} 74 1",
                    AllowedGroups = ["admin"],
                    Description = "复制一个铂金币"
                }
            }
        }
    };
}

/// <summary>
/// 全局设置类，定义插件的默认行为参数
/// </summary>
public sealed class GlobalSettings
{
    [JsonProperty("启用浮动文本")]
    public bool EnableFloatText { get; set; } = true;

    [JsonProperty("启用信息栏文本")]
    public bool EnableChatText { get; set; } = true;

    [JsonProperty("启用自动命令")]
    public bool EnableCommand { get; set; } = true;

    /// <summary>
    /// 手持物品切换后的全局冷却时间（秒）
    /// 作用：防止玩家快速滚轮切换武器导致消息刷屏
    /// </summary>
    [JsonProperty("手持切换冷却(秒)")]
    public double SwitchCooldown { get; set; } = 1.5;

    [JsonProperty("默认浮动文本冷却(秒)")]
    public double FloatTextCooldown { get; set; } = 3.0;

    [JsonProperty("默认信息栏冷却(秒)")]
    public double ChatTextCooldown { get; set; } = 2.0;

    [JsonProperty("默认命令冷却(秒)")]
    public double CommandCooldown { get; set; } = 5.0;

    [JsonProperty("默认Y轴偏移(像素)")]
    public float DefaultYOffset { get; set; } = 50f;

    [JsonProperty("冷却提示消息")]
    public string CooldownMessage { get; set; } = "该{type}冷却中，请在{seconds:F1}秒后再试！";

    [JsonProperty("切换冷却提示")]
    public string SwitchCooldownMessage { get; set; } = "切换过于频繁，请等待{seconds:F1}秒";
}

/// <summary>
/// 单个物品的定义，包含该物品的所有触发配置
/// </summary>
public sealed class ItemDefinition
{
    [JsonProperty("物品名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 浮动文本消息列表（头顶显示），支持随机选择
    /// </summary>
    [JsonProperty("浮动消息列表")]
    public List<MessageData> FloatMessages { get; set; } = new();

    /// <summary>
    /// 信息栏（聊天栏）消息列表，仅玩家自己可见
    /// </summary>
    [JsonProperty("信息栏消息列表")]
    public List<MessageData> ChatMessages { get; set; } = new();

    /// <summary>
    /// 手持该物品时自动执行的命令配置
    /// </summary>
    [JsonProperty("命令配置")]
    public CommandSettings Command { get; set; } = new();

    /// <summary>
    /// 可选的覆盖设置，如果不设置则使用全局默认值
    /// </summary>
    [JsonProperty("自定义覆盖设置")]
    public OverrideSettings? OverrideSettings { get; set; }
}

/// <summary>
/// 消息数据，包含文本内容和颜色
/// </summary>
public sealed class MessageData
{
    [JsonProperty("文本")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// RGB颜色数组，长度应为3，范围0-255
    /// 如 [255, 0, 0] 表示红色
    /// </summary>
    [JsonProperty("颜色", ItemConverterType = typeof(StringEnumConverter))]
    public int[] Color { get; set; } = [255, 255, 255];
}

/// <summary>
/// 命令设置，定义手持物品时执行的TShock命令
/// </summary>
public sealed class CommandSettings
{
    [JsonProperty("启用")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 命令文本，支持变量替换：
    /// {player} = 玩家名, {item} = 物品ID, {x}/{y} = 玩家坐标
    /// </summary>
    [JsonProperty("命令")]
    public string Cmd { get; set; } = string.Empty;

    /// <summary>
    /// 允许执行此命令的权限组列表，空列表表示允许所有组
    /// </summary>
    [JsonProperty("允许的权限组")]
    public List<string> AllowedGroups { get; set; } = new();

    [JsonProperty("描述")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 覆盖设置，用于覆盖全局配置中的特定参数
/// </summary>
public sealed class OverrideSettings
{
    [JsonProperty("Y轴偏移")]
    public float YOffset { get; set; }

    [JsonProperty("浮动文本冷却")]
    public double FloatTextCooldown { get; set; }

    [JsonProperty("信息栏冷却")]
    public double ChatTextCooldown { get; set; }

    [JsonProperty("命令冷却")]
    public double CommandCooldown { get; set; }
}
