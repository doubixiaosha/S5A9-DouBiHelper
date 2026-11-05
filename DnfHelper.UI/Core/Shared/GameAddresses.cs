namespace DnfHelper.UI.Core.Shared
{
    /// <summary>
    /// S5A9（ARAD）地址与偏移。数值只增不删，保持既有地址稳定。
    /// </summary>
    internal static class GameAddresses
    {
        // ---- 全局槽位 ----
        internal const uint PlayerBase = 0x0643A498;   // [槽] 人物对象指针
        internal const uint PlayerPosX = 0x062EFEE4;   // 人物 x（+4 = y）
        internal const uint GameStateAddr = 0x06209FF4; // 0 选角色 / 1 城镇 / 2 选图 / 3 副本 / 5 频道
        internal const uint CharLevel = 0x062D6BF4;
        internal const uint CharNamePtr = 0x062D6BD4; // 角色名称：此地址存指针 -> Unicode 串（二次解引用）
        internal const uint CharJobPtr = 0x062D6BD8;   // 职业名称：此地址存指针 -> Unicode 串（二次解引用）
        internal const uint CurDungeonId = 0x062D6BA4; // 当前副本编号（仅游戏状态 3 有效）
        internal const uint CurTownAddr = 0x062C6A40;
        internal const uint CurAreaAddr = 0x062C6A44;
        internal const uint DecryptBase = 0x06477618;
        internal const uint BagBase = 0x062EF540;
        internal const uint RoomBase = 0x062EF534;
        internal const uint SendBase = 0x0646CDD4;     // [槽] 发包管理器 this
        internal const uint AutoPickFlag = 0x02CA6F76; // 2 字节：74 12 -> 34 01
        internal const uint GmPermissionMode = 0x016F8020; // 本地GM 权限桩：16 字节代码补丁
        internal const uint RoleBase = 0x062EE560;         // [槽] 角色对象指针（GM 权限桩的比较值）

        // ---- 人物 / 地图 ----
        internal const int OffMap = 0xD4;          // 人物 -> 地图对象（0 = 城镇）
        internal const int OffDoorOpen = 0x118;    // 地图对象 -> 开门标志（超级解密，0 = 开）

        // ---- 实体链 ----
        internal const int OffEntityHead = 0xC0;
        internal const int OffEntityTail = 0xC4;
        internal const int OffEntityType = 0xB4;
        internal const int OffEntityDir = 0xC4;    // 实体 -> 位置块
        internal const int OffDirCoord = 0x10;     // 位置块 +0x10/0x14/0x18 = x/y/z float
        internal const int OffEntityPickupKey = 184; // 实体 -> 加密入包 key（超级解密）
        internal const int OffMonsterName = 1108;  // 实体 -> 名称指针
        internal const int EntityItem = 289;       // 地面物品实体类型

        // ---- 背包 / 金库 / 装备 ----
        internal const int OffBagItemsPtr = 0x58;  // 背包对象 +0x58 -> 物品容器
        internal const int OffBagItemsHead = 36;   // 容器 +36 -> 物品指针数组（人物背包）
        internal const int OffBagList = 88;        // 金库容器 +88 -> 槽位数组首
        internal const int OffBagVault = 4;        // 背包基址 +4 -> 普通金库对象
        internal const int BagSlotFirst = 57;      // 人物背包展示首槽（前 56 为装备区）
        internal const int BagSlotLast = 310;      // 57..310 = 254 格
        internal const int VaultSlotFirst = 1;
        internal const int VaultSlotLast = 264;    // 展示上限
        internal const int VaultUsableSlots = 200; // 可用槽位上限
        internal const int EquipSlotCount = 56;
        internal const int EquipSlotProto = 8;     // 发包槽位基数

        internal const int OffItemId = 32;
        internal const int OffItemName = 36;
        internal const int OffItemQuality = 376;    // 0x178
        internal const int OffItemCount = 752;      // 超级解密

        // ---- 房间矩形 / 网格 ----
        internal const uint OffRoomTime = 0x20A030;
        internal const uint OffRoomPathHead = 0x8C;
        internal const uint OffRoomRectBase = 0x1A18;
        internal const int RoomRectStride = 44;
        internal const int OffDoorType = 200;
        internal const int OffAStar1 = 828;
        internal const int OffAStar2 = 848;
        internal const int OffAStar5 = 540;
        internal const int OffCurRoomX = 3376;
        internal const int OffCurRoomY = 3380;
        internal const int OffBossRoomX = 3560;
        internal const int OffBossRoomY = 3568;
        internal const int OffMapIndex = OffBossRoomX - 24;

        // ---- 游戏 CALL ----
        internal const uint CallBuffer = 0x03BDF610;
        internal const uint CallEncrypt = 0x03BDF720; // +48 = 2B，+96 = 4B
        internal const uint CallSend = 0x03BE0FF0;
        internal const uint CallCoordMove = 0x02A5FAB0;
        internal const uint CallForceRoomMove = 0x02D2FEE0;

        // ---- BUFF / 物品 CALL ----
        internal const uint BuffP1 = 0x01ED4920;
        internal const uint BuffP2 = 0x01ED10C0;
        internal const uint BuffP3 = 0x01ECC260;
        internal const uint BuffMem = 0x00400F00;    // 游戏进程空白区，BUFFCALL 复用
        internal const uint ItemCallOffset = 2296;   // 人物对象 -> 物品 CALL 虚表偏移

        // ---- 游戏内彩色公告 ----
        // 商店基址槽。
        internal const uint AnnounceShopBase = 0x062EF53C;
        // 公告 CALL 入口。
        internal const uint AnnounceCall = 0x02E2C9E0;
        // this = [[商店基址] + 0x54]。
        internal const int AnnounceThisOffset = 0x54;

        // ---- 客户端 -> 服务端 CMD ----
        internal const int CmdVaultMove = 19;        // MOVE_ITEMSPACE（存仓 28B）
        internal const int CmdOrganizeBag = 20;
        internal const int CmdSellItem = 22;
        internal const int CmdDisassemble = 26;
        internal const int CmdPickup = 43;           // GET_ITEM（20B）
        internal const int CmdMoveRoom = 45;         // MOVE_ROOM（100B）
    }
}
