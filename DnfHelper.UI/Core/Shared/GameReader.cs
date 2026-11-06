using System;
using System.Collections.Generic;
using DnfHelper.UI.Core.Base;

namespace DnfHelper.UI.Core.Shared
{
    internal struct Rect
    {
        internal float X;
        internal float Y;
        internal float Width;
        internal float Height;
        internal bool Valid;
    }

    internal struct RoomCoord
    {
        internal int X;
        internal int Y;
    }

    internal struct ItemView
    {
        internal int Slot;
        internal uint Pointer;
        internal int Id;
        internal int Quality;
        internal int Count;
        internal string Name;
    }

    internal struct DropView
    {
        internal uint Pointer;
        internal int Key;
        internal float X;
        internal float Y;
        internal string Name;
    }

    /// <summary>角色信息：名称/等级/职业/状态，以及城镇或副本的位置信息。</summary>
    internal struct CharacterView
    {
        internal bool Valid;
        internal string Name;
        internal string Job;
        internal int Level;
        internal int State;
        internal int Town;
        internal int Area;
        internal float X;
        internal float Y;
        internal int DungeonId;
        internal string DungeonName;
        internal bool HasRoom;
        internal int RoomX;
        internal int RoomY;
        internal bool HasBoss;
        internal int BossX;
        internal int BossY;
    }

    /// <summary>
    /// 玩家/地图/背包/金库/装备/掉落/房间矩形/网格通行的读取。
    /// </summary>
    internal static class GameReader
    {
        private const int MaxRoomSide = 512;

        private static readonly int[] DirSet =
        {
            0, 2, 4, 6, 1, 3, 5, 7, 8, 10, 12, 14, 9, 11, 13, 15,
        };

        private static RemoteProcess P => RemoteProcess.Instance;

        private static bool ValidPointer(uint pointer)
        {
            return pointer >= 0x10000 && pointer < 0x80000000 && (pointer & 3) == 0;
        }

        /// <summary>
        /// 当前角色 ID：读 [[PlayerBase] + OffCharacterId] 的 u32（实测确认的玩家对象 +0x6DC8）。
        /// 玩家对象为 0（未进入角色）时直接返回 0，不读任何地址。
        /// 取值必须落在 1..65535，否则视为未取到；返回 0 交由手动输入兜底。
        /// </summary>
        internal static ushort CharacterId()
        {
            var player = Player();
            if (player == 0)
            {
                return 0;
            }

            var address = player + GameAddresses.OffCharacterId;
            if (!P.IsReadable(address, 4))
            {
                return 0;
            }
            var value = P.ReadDword(address);
            if (value < 1 || value > 0xFFFF)
            {
                return 0;
            }

            return (ushort)value;
        }

        // 地图网格：p2 = [[RoomBase + OffRoomTime] + OffDoorType]。
        private static uint GridContext()
        {
            var room = P.ReadDword(GameAddresses.RoomBase);
            if (room == 0)
            {
                return 0;
            }
            var t = P.ReadDword(room + GameAddresses.OffRoomTime);
            if (!ValidPointer(t) || !P.IsReadable(t + GameAddresses.OffDoorType, 4))
            {
                return 0;
            }
            var p2 = P.ReadDword(t + GameAddresses.OffDoorType);
            return ValidPointer(p2) ? p2 : 0;
        }

        private static void DecodeChannel(int channel, out bool left, out bool right, out bool up, out bool down)
        {
            var value = channel >= 0 && channel <= 15 ? DirSet[channel] : 0;
            left = (value & 1) != 0;
            right = (value & 2) != 0;
            up = (value & 4) != 0;
            down = (value & 8) != 0;
        }

        internal static uint Player()
        {
            return P.ReadDword(GameAddresses.PlayerBase);
        }

        internal static uint Map()
        {
            var player = Player();
            return player == 0 ? 0 : P.ReadDword(player + GameAddresses.OffMap);
        }

        internal static bool InDungeon()
        {
            return GameState() == 3;
        }

        internal static int GameState()
        {
            return (int)P.ReadDword(GameAddresses.GameStateAddr);
        }

        internal static float PlayerX()
        {
            return P.ReadFloat(GameAddresses.PlayerPosX);
        }

        internal static float PlayerY()
        {
            return P.ReadFloat(GameAddresses.PlayerPosX + 4);
        }

        internal static int Level()
        {
            return (int)P.ReadDword(GameAddresses.CharLevel);
        }

        internal static int Town()
        {
            return (int)P.ReadDword(GameAddresses.CurTownAddr);
        }

        internal static int Area()
        {
            return (int)P.ReadDword(GameAddresses.CurAreaAddr);
        }

        /// <summary>角色名：读 CharNamePtr 存的名字指针 -> Unicode 串（与职业同一读法）。</summary>
        internal static string PlayerName()
        {
            return ReadText(P.ReadDword(GameAddresses.CharNamePtr), 64);
        }

        /// <summary>职业名：CharJobPtr 存指针 -> Unicode 串。</summary>
        internal static string PlayerJob()
        {
            return ReadText(P.ReadDword(GameAddresses.CharJobPtr), 64);
        }

        /// <summary>当前副本编号：仅游戏状态 3（副本内）读取，其余相位未验证。</summary>
        internal static int DungeonId()
        {
            return (int)P.ReadDword(GameAddresses.CurDungeonId);
        }

        /// <summary>地图名：p2 + OffAStar5 -> Unicode 指针（可能为空）。</summary>
        internal static string MapName()
        {
            var p2 = GridContext();
            if (p2 == 0)
            {
                return string.Empty;
            }
            return ReadText(P.ReadDword(p2 + GameAddresses.OffAStar5), 100);
        }

        /// <summary>
        /// 地图尺寸：p2 + kOffMapIndex(3536) 超级解密取地图索引，
        /// 尺寸表 [p2 + kOffAStar1(828)] + 索引*8 的前两个 dword = 宽 / 高。
        /// </summary>
        private static bool TryMapSize(uint p2, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (p2 == 0)
            {
                return false;
            }
            var mapIndex = Codec.SuperDecrypt(p2 + GameAddresses.OffMapIndex);
            if (mapIndex < 0)
            {
                return false;
            }
            var sizeTable = P.ReadDword(p2 + GameAddresses.OffAStar1);
            if (!ValidPointer(sizeTable))
            {
                return false;
            }
            var address = sizeTable + (uint)mapIndex * 8;
            if (!P.IsReadable(address, 8))
            {
                return false;
            }
            width = (int)P.ReadDword(address);
            height = (int)P.ReadDword(address + 4);
            return width > 0 && height > 0 && width <= MaxRoomSide && height <= MaxRoomSide;
        }

        /// <summary>坐标必须落在网格宽高内；尺寸不可得时不额外限制。</summary>
        private static bool InMapBounds(uint p2, RoomCoord coord)
        {
            int width, height;
            if (!TryMapSize(p2, out width, out height))
            {
                return true;
            }
            return coord.X >= 0 && coord.Y >= 0 && coord.X < width && coord.Y < height;
        }

        /// <summary>BOSS（领主）房坐标：超级解密读，负值视为失败。</summary>
        internal static bool ReadBossRoom(out RoomCoord coord)
        {
            return ReadBossRoomAt(GridContext(), out coord);
        }

        /// <summary>在指定上下文中读 BOSS 房坐标（8 字节加密槽位、4 字节值）。</summary>
        internal static bool ReadBossRoomAt(uint context, out RoomCoord coord)
        {
            coord = new RoomCoord();
            if (context == 0 || !P.IsReadable(context + GameAddresses.OffBossRoomY, 4))
            {
                return false;
            }
            var bx = Codec.SuperDecrypt(context + GameAddresses.OffBossRoomX);
            var by = Codec.SuperDecrypt(context + GameAddresses.OffBossRoomY);
            // 只拒负值（解密失败返回 -1）；(0,0) 是合法坐标。
            if (bx < 0 || by < 0)
            {
                return false;
            }
            coord.X = bx;
            coord.Y = by;
            return true;
        }

        /// <summary>在指定上下文中读当前房间坐标（明文 int）。</summary>
        internal static bool ReadCurRoomAt(uint context, out RoomCoord coord)
        {
            coord = new RoomCoord();
            if (context == 0 || !P.IsReadable(context + GameAddresses.OffCurRoomY, 4))
            {
                return false;
            }
            coord.X = (int)P.ReadDword(context + GameAddresses.OffCurRoomX);
            coord.Y = (int)P.ReadDword(context + GameAddresses.OffCurRoomY);
            return true;
        }

        /// <summary>一次性取回角色信息（名称/等级/职业/状态 + 城镇或副本位置）。</summary>
        internal static CharacterView ReadCharacter()
        {
            var view = new CharacterView { Name = string.Empty, Job = string.Empty, DungeonName = string.Empty };
            if (!P.Attached)
            {
                return view;
            }

            view.Valid = true;
            view.Name = PlayerName();
            view.Job = PlayerJob();
            view.Level = Level();
            view.State = GameState();
            view.Town = Town();
            view.Area = Area();
            view.X = PlayerX();
            view.Y = PlayerY();

            if (view.State == 3)
            {
                view.DungeonId = DungeonId();
                view.DungeonName = MapName();
                // 两者都用 MapPtr() 的 p2 网格上下文：
                // 当前房明文读，领主房超级解密读；顺图 CanStep 也走同一个 p2。
                var p2 = GridContext();
                RoomCoord room;
                if (ReadCurRoomAt(p2, out room) && InMapBounds(p2, room))
                {
                    view.HasRoom = true;
                    view.RoomX = room.X;
                    view.RoomY = room.Y;
                }
                RoomCoord boss;
                if (ReadBossRoomAt(p2, out boss) && InMapBounds(p2, boss))
                {
                    view.HasBoss = true;
                    view.BossX = boss.X;
                    view.BossY = boss.Y;
                }
            }
            return view;
        }

        internal static uint BagStorage()
        {
            var bag = P.ReadDword(GameAddresses.BagBase);
            if (bag == 0)
            {
                return 0;
            }
            var list = P.ReadDword(bag + GameAddresses.OffBagList);
            return ValidPointer(list) ? list + GameAddresses.OffBagItemsHead : 0;
        }

        internal static uint VaultStorage()
        {
            var vault = P.ReadDword(GameAddresses.BagBase + GameAddresses.OffBagVault);
            if (vault == 0)
            {
                return 0;
            }
            var list = P.ReadDword(vault + GameAddresses.OffBagList);
            return ValidPointer(list) ? list : 0;
        }

        internal static string ReadName(uint pointer)
        {
            if (!ValidPointer(pointer))
            {
                return string.Empty;
            }
            string name;
            return P.ReadWideString(pointer, 52, out name) ? name : string.Empty;
        }

        /// <summary>按字符上限读取 Unicode 串（角色名/职业名/地图名用各自的长度上限）。</summary>
        private static string ReadText(uint pointer, int maxChars)
        {
            if (!ValidPointer(pointer))
            {
                return string.Empty;
            }
            string text;
            return P.ReadWideString(pointer, maxChars, out text) ? text : string.Empty;
        }

        internal static List<ItemView> ScanSlots(uint storage, int firstSlot, int lastSlot)
        {
            var result = new List<ItemView>();
            if (!ValidPointer(storage) || firstSlot <= 0 || lastSlot < firstSlot)
            {
                return result;
            }

            for (var slot = firstSlot; slot <= lastSlot; slot++)
            {
                var view = new ItemView { Slot = slot, Name = string.Empty };
                var cell = storage + (uint)((slot - 1) * 4);
                if (!P.IsReadable(cell, 4))
                {
                    result.Add(view);
                    continue;
                }
                var pointer = P.ReadDword(cell);
                if (!ValidPointer(pointer) || !P.IsReadable(pointer + GameAddresses.OffItemName, 4))
                {
                    result.Add(view);
                    continue;
                }

                view.Pointer = pointer;
                view.Id = (int)P.ReadDword(pointer + GameAddresses.OffItemId);
                view.Quality = (int)P.ReadDword(pointer + GameAddresses.OffItemQuality);
                view.Count = (int)Codec.SuperDecrypt(pointer + GameAddresses.OffItemCount);
                var namePointer = P.ReadDword(pointer + GameAddresses.OffItemName);
                view.Name = ReadName(namePointer);
                result.Add(view);
            }
            return result;
        }

        internal static List<ItemView> ScanBag()
        {
            return ScanSlots(BagStorage(), GameAddresses.BagSlotFirst, GameAddresses.BagSlotLast);
        }

        internal static List<ItemView> ScanVault()
        {
            return ScanSlots(VaultStorage(), GameAddresses.VaultSlotFirst, GameAddresses.VaultSlotLast);
        }

        internal static List<ItemView> ScanEquipment()
        {
            return ScanSlots(BagStorage(), 1, GameAddresses.EquipSlotCount);
        }

        internal static List<uint> CollectItems(int maxCount)
        {
            var result = new List<uint>();
            var map = Map();
            if (map == 0)
            {
                return result;
            }

            var head = P.ReadDword(map + GameAddresses.OffEntityHead);
            var tail = P.ReadDword(map + GameAddresses.OffEntityTail);
            if (head == 0 || tail <= head || !P.IsReadable(head, 4) || !P.IsReadable(tail - 4, 4))
            {
                return result;
            }

            var count = (int)((tail - head) / 4);
            if (count <= 0)
            {
                return result;
            }
            if (count > 512)
            {
                count = 512;
            }

            for (var i = 0; i < count && result.Count < maxCount; i++)
            {
                var pointer = P.ReadDword(head + (uint)i * 4);
                if (!ValidPointer(pointer) || !P.IsReadable(pointer, GameAddresses.OffEntityType + 4))
                {
                    continue;
                }
                if ((int)P.ReadDword(pointer + GameAddresses.OffEntityType) != GameAddresses.EntityItem)
                {
                    continue;
                }
                result.Add(pointer);
            }
            return result;
        }

        internal static DropView MakeDrop(uint pointer)
        {
            var drop = new DropView { Pointer = pointer, Name = string.Empty };
            drop.Key = (int)Codec.SuperDecrypt(pointer + GameAddresses.OffEntityPickupKey);
            var block = P.ReadDword(pointer + GameAddresses.OffEntityDir);
            if (ValidPointer(block))
            {
                drop.X = P.ReadFloat(block + GameAddresses.OffDirCoord);
                drop.Y = P.ReadFloat(block + GameAddresses.OffDirCoord + 4);
            }
            var namePointer = P.ReadDword(pointer + GameAddresses.OffMonsterName);
            drop.Name = ReadName(namePointer);
            return drop;
        }

        internal static bool DropCoord(DropView drop, out float x, out float y)
        {
            x = 0.0f;
            y = 0.0f;
            var block = P.ReadDword(drop.Pointer + GameAddresses.OffEntityDir);
            if (!ValidPointer(block))
            {
                return false;
            }
            x = P.ReadFloat(block + GameAddresses.OffDirCoord);
            y = P.ReadFloat(block + GameAddresses.OffDirCoord + 4);
            return true;
        }

        internal static bool ReadDoorOpen()
        {
            var map = Map();
            if (map == 0 || !P.IsReadable(map + GameAddresses.OffDoorOpen, 4))
            {
                return false;
            }
            return Codec.SuperDecrypt(map + GameAddresses.OffDoorOpen) == 0;
        }

        internal static bool ReadRoomRect(int dir, out Rect rect)
        {
            rect = new Rect();
            if (dir < 0 || dir > 3)
            {
                return false;
            }
            var room = P.ReadDword(GameAddresses.RoomBase);
            if (room == 0)
            {
                return false;
            }
            var t = P.ReadDword(room + GameAddresses.OffRoomTime);
            if (!ValidPointer(t) || !P.IsReadable(t, 4))
            {
                return false;
            }
            var p = P.ReadDword(t + GameAddresses.OffRoomPathHead);
            if (!ValidPointer(p))
            {
                return false;
            }
            var address = p + (uint)dir * GameAddresses.RoomRectStride + GameAddresses.OffRoomRectBase;
            if (!P.IsReadable(address, 16))
            {
                return false;
            }

            rect.X = (float)(int)P.ReadDword(address);
            rect.Y = (float)(int)P.ReadDword(address + 4);
            rect.Width = (float)(int)P.ReadDword(address + 8);
            rect.Height = (float)(int)P.ReadDword(address + 12);
            rect.Valid = rect.Width > 0 && rect.Height > 0 && rect.Width <= 4096 && rect.Height <= 4096;
            return rect.Valid;
        }

        internal static bool ReadCurRoom(out RoomCoord coord)
        {
            coord = new RoomCoord();
            var p2 = GridContext();
            if (p2 == 0 || !P.IsReadable(p2 + GameAddresses.OffCurRoomY, 4))
            {
                return false;
            }
            coord.X = (int)P.ReadDword(p2 + GameAddresses.OffCurRoomX);
            coord.Y = (int)P.ReadDword(p2 + GameAddresses.OffCurRoomY);
            return true;
        }

        /// <summary>
        /// 单步通行判定：读**当前格**的通道字节，
        /// 按方向取位（left=bit0 right=bit1 up=bit2 down=bit3）；网格不可读时不拦截。
        /// 目标格越界不判失败——跨房间走门是合法的。
        /// </summary>
        internal static bool CanStep(RoomCoord current, int dir, out RoomCoord next)
        {
            next = current;
            switch (dir)
            {
                case 0: next.X -= 1; break;
                case 1: next.X += 1; break;
                case 2: next.Y -= 1; break;
                case 3: next.Y += 1; break;
                default:
                    return false;
            }

            // 目标格出现负坐标直接放弃（不发送）。
            if (next.X < 0 || next.Y < 0)
            {
                return false;
            }
            var p2 = GridContext();
            if (p2 == 0)
            {
                return true;
            }
            var index = Codec.SuperDecrypt(p2 + GameAddresses.OffMapIndex);
            if (index < 0)
            {
                return true;
            }

            var sizeTab = P.ReadDword(p2 + GameAddresses.OffAStar1);
            if (!ValidPointer(sizeTab) || !P.IsReadable(sizeTab + (uint)index * 8, 8))
            {
                return true;
            }
            var width = (int)P.ReadDword(sizeTab + (uint)index * 8);
            var height = (int)P.ReadDword(sizeTab + (uint)index * 8 + 4);
            if (width <= 0 || height <= 0 || width > MaxRoomSide || height > MaxRoomSide)
            {
                return true;
            }

            // 只要求当前格在网格内；目标格可以越界（走门去相邻房间）。
            if (current.X < 0 || current.Y < 0 || current.X >= width || current.Y >= height)
            {
                return false;
            }

            var channelTab = P.ReadDword(p2 + GameAddresses.OffAStar2);
            if (!ValidPointer(channelTab))
            {
                return true;
            }
            var channelCell = channelTab + (uint)index * 20 + 4;
            if (!P.IsReadable(channelCell, 4))
            {
                return true;
            }
            var channelBase = P.ReadDword(channelCell);
            if (!ValidPointer(channelBase))
            {
                return true;
            }

            // 关键：通道字节取当前格（通道数组按 y*width + x 索引）。
            var cell = channelBase + (uint)(current.Y * width + current.X) * 4;
            if (!P.IsReadable(cell, 4))
            {
                return true;
            }
            var channel = (int)P.ReadDword(cell);
            if (channel <= 0 || channel > 15)
            {
                return false;
            }

            bool left, right, up, down;
            DecodeChannel(channel, out left, out right, out up, out down);
            switch (dir)
            {
                case 0: return left;
                case 1: return right;
                case 2: return up;
                default: return down;
            }
        }
    }
}
