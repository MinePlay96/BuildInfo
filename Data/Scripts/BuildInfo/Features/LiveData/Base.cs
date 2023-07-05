﻿using System;
using System.Collections.Generic;
using Digi.BuildInfo.Features.Overlays;
using Digi.BuildInfo.Utilities;
using Digi.BuildInfo.VanillaData;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace Digi.BuildInfo.Features.LiveData
{
    [Flags]
    public enum ConveyorFlags : byte
    {
        None = 0,
        Small = (1 << 0),
        In = (1 << 1),
        Out = (1 << 2),
        Interactive = (1 << 3),
        Unreachable = (1 << 4),
    }

    public struct ConveyorInfo
    {
        public readonly ConveyorFlags Flags;
        public readonly Matrix LocalMatrix;
        Vector3I CellPosition;
        Base6Directions.Direction Direction;

        public ConveyorInfo(ConveyorFlags flags, Matrix localMatrix, Vector3I cellPos, Base6Directions.Direction dir)
        {
            Utils.MatrixMinSize(ref localMatrix, BData_Base.MatrixAllMinScale, BData_Base.MatrixIndividualMinScale);

            Flags = flags;
            LocalMatrix = localMatrix;
            CellPosition = cellPos;
            Direction = dir;
        }

        public void TransformToGrid(IMySlimBlock block, out Vector3I gridPos, out Base6Directions.Direction gridDir)
        {
            // from MyConveyorConnector.PositionToGridCoords()

            //Matrix localOrientation;
            //block.Orientation.GetMatrix(out localOrientation);
            //gridPos = Vector3I.Round(Vector3.TransformNormal(new Vector3(CellPosition), localOrientation)) + block.Position;

            // optimized with integer transform
            MatrixI matrix = new MatrixI(block.Position, block.Orientation.Forward, block.Orientation.Up);
            Vector3I.Transform(ref CellPosition, ref matrix, out gridPos);

            gridDir = block.Orientation.TransformDirection(Direction);
        }
    }

    public struct InteractionInfo
    {
        public readonly Matrix LocalMatrix;
        public readonly string Name;
        public readonly Color Color;

        public InteractionInfo(Matrix localMatrix, string name, Color color)
        {
            Utils.MatrixMinSize(ref localMatrix, BData_Base.MatrixAllMinScale, BData_Base.MatrixIndividualMinScale);

            LocalMatrix = localMatrix;
            Name = name;
            Color = color;
        }
    }

    public struct SubpartInfo
    {
        //public readonly string Name;
        public readonly Matrix LocalMatrix;
        public readonly string Model;
        public readonly List<SubpartInfo> Subparts;

        public SubpartInfo(Matrix localMatrix, string model, List<SubpartInfo> subparts)
        {
            LocalMatrix = localMatrix;
            Model = model;
            Subparts = subparts;
        }
    }

    [Flags]
    public enum BlockHas : byte
    {
        Nothing = 0,

        /// <summary>
        /// Block type supports connecting to conveyor network.
        /// </summary>
        ConveyorSupport = (1 << 0),

        /// <summary>
        /// Block has a terminal (is IMyTerminalBlock)
        /// </summary>
        Terminal = (1 << 1),

        /// <summary>
        /// Block has at least one inventory.
        /// </summary>
        Inventory = (1 << 2),

        /// <summary>
        /// Block model has interactive access to terminal/inventory
        /// </summary>
        PhysicalTerminalAccess = (1 << 3),

        /// <summary>
        /// Block has detector_ownership in the model which enables ownership support (but rather buggy, especially if it's not present in construction model).
        /// </summary>
        OwnershipDetector = (1 << 4),

        /// <summary>
        /// Block has custom useobjects or custom gamelogic.
        /// </summary>
        //CustomLogic = (1 << 5),
    }

    public class BData_Base
    {
        public const float MatrixAllMinScale = 0.2f;
        public const float MatrixIndividualMinScale = 0.05f;

        public BlockHas Has = BlockHas.Nothing;
        public List<ConveyorInfo> ConveyorPorts;
        public List<InteractionInfo> Interactive;
        public List<Matrix> UpgradePorts;
        public List<string> Upgrades;
        public List<SubpartInfo> Subparts;

        public BData_Base()
        {
        }

        public bool CheckAndAdd(IMyCubeBlock block)
        {
            MyCubeBlockDefinition def = (MyCubeBlockDefinition)block.SlimBlock.BlockDefinition;

            Dictionary<string, IMyModelDummy> dummies = BuildInfoMod.Instance.Caches.Dummies;
            dummies.Clear();
            block.Model.GetDummies(dummies);

            ComputeSubparts(block);
            ComputeUpgrades(block);
            ComputeHas(block);
            ComputeDummies(block, def, dummies);
            //ComputeUseObjects(block, def);

            dummies.Clear();

            bool isValid = IsValid(block, def);

            // now always valid because of the base data

            BuildInfoMod.Instance.LiveDataHandler.BlockData.Add(def.Id, this);
            return true;
        }

        protected virtual bool IsValid(IMyCubeBlock block, MyCubeBlockDefinition def) => false;

        void ComputeUpgrades(IMyCubeBlock block)
        {
            MyCubeBlock internalBlock = (MyCubeBlock)block;
            if(internalBlock.UpgradeValues.Count > 0)
            {
                Upgrades = new List<string>(internalBlock.UpgradeValues.Count);

                foreach(string upgrade in internalBlock.UpgradeValues.Keys)
                {
                    Upgrades.Add(upgrade);
                }
            }
        }

        void ComputeSubparts(IMyCubeBlock block)
        {
            MyEntity ent = (MyEntity)block;

            if(ent.Subparts != null && ent.Subparts.Count > 0)
            {
                Subparts = new List<SubpartInfo>(ent.Subparts.Count);
                RecursiveSubpartScan(ent, Subparts);
            }
        }

        static void RecursiveSubpartScan(MyEntity entity, List<SubpartInfo> addTo)
        {
            // HACK: make the first layer of subparts be relative to centered block matrix, not ModelOffset'd one
            MatrixD? transform = null;
            IMyCubeBlock block = entity as IMyCubeBlock;
            if(block != null)
                transform = MatrixD.Invert(Utils.GetBlockCenteredWorldMatrix(block.SlimBlock));

            foreach(MyEntitySubpart subpart in entity.Subparts.Values)
            {
                IMyModel model = (IMyModel)subpart.Model;

                Matrix localMatrix;
                if(transform.HasValue)
                    localMatrix = (Matrix)(subpart.PositionComp.WorldMatrixRef * transform.Value);
                else
                    localMatrix = subpart.PositionComp.LocalMatrixRef;

                SubpartInfo info;
                if(subpart.Subparts != null && subpart.Subparts.Count > 0)
                {
                    info = new SubpartInfo(localMatrix, model.AssetName, new List<SubpartInfo>(subpart.Subparts.Count));
                    RecursiveSubpartScan(subpart, info.Subparts);
                }
                else
                {
                    info = new SubpartInfo(localMatrix, model.AssetName, null);
                }

                addTo.Add(info);
            }
        }

        void ComputeHas(IMyCubeBlock block)
        {
            MyCubeBlock internalBlock = (MyCubeBlock)block;

            if(BuildInfoMod.Instance.LiveDataHandler.ConveyorSupportTypes.GetValueOrDefault(block.BlockDefinition.TypeId, false))
                Has |= BlockHas.ConveyorSupport;

            if(block is IMyTerminalBlock)
                Has |= BlockHas.Terminal;

            if(block.HasInventory)
                Has |= BlockHas.Inventory;

            // HACK: from MyCubeBlock.InitOwnership()
            if(internalBlock.UseObjectsComponent != null && internalBlock.UseObjectsComponent.GetDetectors("ownership").Count > 0)
                Has |= BlockHas.OwnershipDetector;
        }

        void ComputeDummies(IMyCubeBlock block, MyCubeBlockDefinition def, Dictionary<string, IMyModelDummy> dummies)
        {
            if(dummies.Count == 0)
                return;

            if(block?.Model == null)
            {
                //Log.Error($"Block '{def.Id.ToString()}' has a FatBlock but no Model, this will crash when opening info tab on its grid; I recommend you add a <Model> tag to it, even if it's a single tiny triangle model.", Log.PRINT_MESSAGE);
                return;
            }

            const StringComparison CompareType = StringComparison.InvariantCultureIgnoreCase;

            Interactive = new List<InteractionInfo>(dummies.Values.Count);

            // supergridding might break this?
            float cellSize = MyDefinitionManager.Static.GetCubeSize(def.CubeSize);
            float cellSizeHalf = cellSize / 2f;
            Vector3 sizeMetric = new Vector3(def.Size) * cellSizeHalf;

            foreach(IMyModelDummy dummy in dummies.Values)
            {
                string name = dummy.Name;
                if(!name.StartsWith("detector_", CompareType))
                    continue;

                Matrix matrix = dummy.Matrix;
                matrix.Translation += def.ModelOffset;

                int index = 9; // "detector_".Length
                StringSegment detectorType = GetNextSection(name, ref index); // detector_<here>_small_in
                TextPtr detectorPtr = new TextPtr(detectorType.Text, detectorType.Start);
                StringSegment part1 = GetNextSection(name, ref index); // detector_conveyorline_<here>_in
                StringSegment part2 = GetNextSection(name, ref index); // detector_conveyorline_small_<here>

                if(detectorPtr.StartsWith("conveyor"))
                {
                    ConveyorFlags flags = ConveyorFlags.None;

                    // from MyConveyorLine.GetBlockLinePositions()
                    if(part1.EqualsIgnoreCase("small"))
                        flags |= ConveyorFlags.Small;

                    // same logic order: 'out' overrides 'in'
                    if(part1.EqualsIgnoreCase("out") || part2.EqualsIgnoreCase("out"))
                        flags |= ConveyorFlags.Out;
                    else if(part1.EqualsIgnoreCase("in") || part2.EqualsIgnoreCase("in"))
                        flags |= ConveyorFlags.In;

                    if(!detectorPtr.StartsWith("conveyorline"))
                    {
                        flags |= ConveyorFlags.Interactive;
                        Has |= BlockHas.PhysicalTerminalAccess;
                    }

                    Vector3 portLocalPos = matrix.Translation + sizeMetric; // + blockDef.ModelOffset  (already done to port matrix)
                    Vector3I clamped = Vector3I.Clamp(Vector3I.Floor(portLocalPos / cellSize), Vector3I.Zero, def.Size - Vector3I.One);
                    Vector3I portCellPos = clamped - def.Center;

                    Vector3 cellV3 = (new Vector3(clamped) + Vector3.Half) * cellSize;
                    Vector3 dirVec = Vector3.DominantAxisProjection((portLocalPos - cellV3) / cellSize);
                    dirVec.Normalize();
                    Base6Directions.Direction portDir = Base6Directions.GetDirection(dirVec);

                    ConveyorInfo port = new ConveyorInfo(flags, matrix, portCellPos, portDir);

                    // some blocks like classic large turrets have their interactive ports as real conveyor ports but they are not reachable because they start from middle cell.
                    {
                        Vector3I gridPos;
                        Base6Directions.Direction gridDir;
                        port.TransformToGrid(block.SlimBlock, out gridPos, out gridDir);

                        IMySlimBlock slimCheck = block.CubeGrid.GetCubeBlock(gridPos + Base6Directions.GetIntVector(gridDir));
                        if(slimCheck == block.SlimBlock)
                        {
                            flags |= ConveyorFlags.Unreachable;
                            port = new ConveyorInfo(flags, matrix, portCellPos, portDir);
                        }
                    }

                    if(ConveyorPorts == null)
                        ConveyorPorts = new List<ConveyorInfo>();

                    ConveyorPorts.Add(port);

                    // less memory to just compute it in overlay, not much added overhead
                    //if((flags & ConveyorFlags.Interactive) != 0)
                    //{
                    //    if((flags & ConveyorFlags.Unreachable) != 0)
                    //    {
                    //        Interactive.Add(new InteractionInfo(matrix, "Inventory/Terminal access", colorTerminalOnly));
                    //    }
                    //    else
                    //    {
                    //        if((flags & ConveyorFlags.Small) != 0)
                    //            Interactive.Add(new InteractionInfo(matrix, "        Interactive\nSmall conveyor port", interactivePortColor));
                    //        else
                    //            Interactive.Add(new InteractionInfo(matrix, "        Interactive\nLarge conveyor port", interactivePortColor));
                    //    }
                    //}
                }
                else if(detectorType.EqualsIgnoreCase("upgrade"))
                {
                    if(UpgradePorts == null)
                        UpgradePorts = new List<Matrix>();

                    Utils.MatrixMinSize(ref matrix, MatrixAllMinScale, MatrixIndividualMinScale);
                    UpgradePorts.Add(matrix);
                }
                // from classes that use MyUseObjectAttribute
                else if(detectorType.EqualsIgnoreCase("terminal"))
                {
                    if(Hardcoded.DetectorIsOpenCloseDoor("terminal", block))
                        Interactive.Add(new InteractionInfo(matrix, "Open/Close\n+Terminal access", OverlayDrawInstance.InteractiveActionOrTerminalColor));
                    else
                        Interactive.Add(new InteractionInfo(matrix, "Terminal/inventory access", OverlayDrawInstance.InteractiveTerminalColor));

                    Has |= BlockHas.PhysicalTerminalAccess;
                }
                else if(detectorType.EqualsIgnoreCase("inventory")
                     || detectorType.EqualsIgnoreCase("vendingMachine") // excluding vendingMachineBuy/vendingMachineNext/vendingMachinePrevious; clicking this one just opens terminal
                     || detectorType.EqualsIgnoreCase("jukebox")) // excluding jukeboxNext/jukeboxPrevious/jukeboxPause; clicking this one just opens terminal
                {
                    Interactive.Add(new InteractionInfo(matrix, "Inventory/Terminal access", OverlayDrawInstance.InteractiveTerminalColor));
                }
                else if(detectorType.EqualsIgnoreCase("textpanel"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Edit LCD\n+Terminal access", OverlayDrawInstance.InteractiveActionOrTerminalColor));
                }
                else if(detectorType.EqualsIgnoreCase("advanceddoor")
                     || detectorType.EqualsIgnoreCase("door"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Open/Close\n+Terminal access", OverlayDrawInstance.InteractiveActionOrTerminalColor));
                    Has |= BlockHas.PhysicalTerminalAccess;
                }
                else if(detectorType.EqualsIgnoreCase("block")) // medical room/survival kit heal
                {
                    Interactive.Add(new InteractionInfo(matrix, "Recharge\n+Terminal access", OverlayDrawInstance.InteractiveActionOrTerminalColor));
                    Has |= BlockHas.PhysicalTerminalAccess;
                }
                else if(detectorType.EqualsIgnoreCase("contract"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Open Contracts\n+Terminal access", OverlayDrawInstance.InteractiveActionOrTerminalColor));
                    Has |= BlockHas.PhysicalTerminalAccess;
                }
                else if(detectorType.EqualsIgnoreCase("store"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Open Store\n+Terminal access", OverlayDrawInstance.InteractiveActionOrTerminalColor));
                    Has |= BlockHas.PhysicalTerminalAccess;
                }
                else if(detectorType.EqualsIgnoreCase("ATM"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Open Transactions\n+Terminal access", OverlayDrawInstance.InteractiveActionOrTerminalColor));
                    Has |= BlockHas.PhysicalTerminalAccess;
                }
                // from here only interactive things that can't open terminal
                else if(detectorType.EqualsIgnoreCase("cockpit")
                     || detectorType.EqualsIgnoreCase("cryopod"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Entrance", OverlayDrawInstance.InteractiveColor));
                }
                else if(detectorType.EqualsIgnoreCase("connector"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Connector (large)", OverlayDrawInstance.InteractiveColor));
                }
                else if(detectorType.EqualsIgnoreCase("small") && part1.EqualsIgnoreCase("connector"))
                {
                    Interactive.Add(new InteractionInfo(matrix, "Connector (small)", OverlayDrawInstance.InteractiveColor));
                }
                else if(detectorType.EqualsIgnoreCase("wardrobe")
                     || detectorType.EqualsIgnoreCase("ladder")
                     || detectorType.EqualsIgnoreCase("respawn") // medical room/survival kit respawn point
                     || detectorType.EqualsIgnoreCase("jukeboxNext")
                     || detectorType.EqualsIgnoreCase("jukeboxPrevious")
                     || detectorType.EqualsIgnoreCase("jukeboxPause")
                     || detectorType.EqualsIgnoreCase("vendingMachineBuy")
                     || detectorType.EqualsIgnoreCase("vendingMachineNext")
                     || detectorType.EqualsIgnoreCase("vendingMachinePrevious")
                     || detectorType.EqualsIgnoreCase("shiptool")
                     || detectorType.EqualsIgnoreCase("merge")
                     || detectorType.EqualsIgnoreCase("collector")
                     || detectorType.EqualsIgnoreCase("ejector")
                     || detectorType.EqualsIgnoreCase("ladder")
                     || detectorPtr.StartsWithCaseInsensitive("panel_button")
                     || detectorPtr.StartsWithCaseInsensitive("textpanel") // does not match the useobject but it's used in emotioncontroller and does nothing, just ignoring it here
                     || detectorPtr.StartsWithCaseInsensitive("maintenance"))
                {
                    // nothing, just ignoring known dummies so I can find new ones
                }
                else if(BuildInfoMod.IsDevMod)
                {
                    Log.Info($"[DEV] Model for {def.Id.ToString()} has unknown dummy '{name}'.");
                }
            }

            dummies.Clear();

            TrimList(ref ConveyorPorts);
            TrimList(ref UpgradePorts);
            TrimList(ref Interactive);
        }

        static void TrimList<T>(ref List<T> list)
        {
            if(list == null)
                return;

            if(list.Count == 0)
            {
                list = null;
                return;
            }

            if(list.Count > list.Capacity)
                list.Capacity = list.Count;
        }

        void ComputeGamelogic(IMyCubeBlock block, MyCubeBlockDefinition def)
        {
#if false
            // block has 2+ gamelogic components
            if(block.GameLogic is MyCompositeGameLogicComponent)
            {
                Has |= BlockHas.CustomLogic;
                return;
            }

            if(block.GameLogic != null)
            {
                // HACK: not very reliable but it'll do
                string classFullName = block.GameLogic.GetType().FullName;
                bool vanillaUseObject = classFullName.StartsWith("SpaceEngineers.") || classFullName.StartsWith("Sandbox.") || classFullName.StartsWith("VRage.");

                if(!vanillaUseObject)
                {
                    Has |= BlockHas.CustomLogic;
                    return;
                }
            }

            MyCubeBlock internalBlock = (MyCubeBlock)block;
            MyUseObjectsComponentBase comp = internalBlock.UseObjectsComponent;
            if(comp != null)
            {
                List<IMyUseObject> useObjects = BuildInfoMod.Instance.Caches.UseObjects;
                useObjects.Clear();
                comp.GetInteractiveObjects(useObjects);

                if(useObjects.Count > 0)
                {
                    foreach(IMyUseObject useObject in useObjects)
                    {
                        // HACK: not very reliable but it'll do
                        string classFullName = useObject.GetType().FullName;
                        bool vanillaUseObject = classFullName.StartsWith("SpaceEngineers.") || classFullName.StartsWith("Sandbox.") || classFullName.StartsWith("VRage.");

                        if(!vanillaUseObject)
                        {
                            Has |= BlockHas.CustomLogic;
                            break;
                        }
                    }

                    useObjects.Clear();
                }
            }
#endif
        }

        static StringSegment GetNextSection(string text, ref int startIndex)
        {
            if(startIndex >= text.Length)
                return default(StringSegment);

            int sepIndex = text.IndexOf('_', startIndex);
            if(sepIndex > -1)
            {
                StringSegment segment = new StringSegment(text, startIndex, sepIndex - startIndex);
                startIndex = sepIndex + 1;
                return segment;
            }
            else
            {
                StringSegment segment = new StringSegment(text, startIndex, text.Length - startIndex);
                startIndex = text.Length;
                return segment;
            }
        }
    }
}