﻿using System;
using System.Collections.Generic;
using Digi.BuildInfo.Features.LiveData;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.BuildInfo.Features.Overlays.ConveyorNetwork
{
    // TODO: explain somewhere how the visuals work
    public class ConveyorNetworkCompute
    {
        public const string NotifyPrefix = "ConveyorVis: ";

        ConveyorNetworkView Handler;
        ConveyorNetworkRender Render;
        LiveDataHandler LiveData;

        class Conveyor
        {
            public BData_Base Data;
            public List<ConnectedPort> Connected = new List<ConnectedPort>();
        }

        struct ConnectedPort
        {
            public int PortIndex;
            public Vector3 PortGridLocalPos;
            public MyCubeBlock OtherBlock;
        }

        GridRender CurrentGridRender;
        Dictionary<MyCubeBlock, Conveyor> TempConveyorData = new Dictionary<MyCubeBlock, Conveyor>();
        HashSet<MyCubeBlock> TempCheckedBlocks = new HashSet<MyCubeBlock>();
        int NetworkIndex = 0;
        Color NetworkColor;

        List<IMyCubeGrid> GridsForEvents = new List<IMyCubeGrid>();

        public ConveyorNetworkCompute(ConveyorNetworkView handler)
        {
            Handler = handler;
        }

        public void Init()
        {
            LiveData = Handler.Main.LiveDataHandler;
            Render = Handler.Render;
        }

        public void Reset()
        {
            UnhookEvents();
            ResetCompute();
        }

        void HookEvents(ICollection<IMyCubeGrid> grids)
        {
            GridsForEvents.AddRange(grids);

            foreach(MyCubeGrid grid in grids)
            {
                IMyCubeGrid apiGrid = grid;

                apiGrid.OnBlockAdded += GridBlockChanges;
                apiGrid.OnBlockRemoved += GridBlockChanges;
                apiGrid.OnBlockIntegrityChanged += GridBlockChanges;
                grid.OnGridMerge += GridMergeOrSplit;
                grid.OnGridSplit += GridMergeOrSplit;
                grid.OnConnectionChangeCompleted += GridConnectionChange;
            }
        }

        void UnhookEvents()
        {
            try
            {
                foreach(MyCubeGrid grid in GridsForEvents)
                {
                    IMyCubeGrid apiGrid = grid;

                    apiGrid.OnBlockAdded -= GridBlockChanges;
                    apiGrid.OnBlockRemoved -= GridBlockChanges;
                    apiGrid.OnBlockIntegrityChanged -= GridBlockChanges;
                    grid.OnGridMerge -= GridMergeOrSplit;
                    grid.OnGridSplit -= GridMergeOrSplit;
                    grid.OnConnectionChangeCompleted -= GridConnectionChange;
                }
            }
            finally
            {
                GridsForEvents.Clear();
            }
        }

        void GridBlockChanges(IMySlimBlock slim) => Handler.ScheduleRescan();
        void GridMergeOrSplit(MyCubeGrid grid1, MyCubeGrid grid2) => Handler.ScheduleRescan();
        void GridConnectionChange(MyCubeGrid grid, GridLinkTypeEnum link) => Handler.ScheduleRescan();

        void ResetCompute()
        {
            NetworkIndex = 0;
            CurrentGridRender = null;
            TempCheckedBlocks.Clear();
            TempConveyorData.Clear();
        }

        public bool FindConveyorNetworks(ICollection<IMyCubeGrid> grids, IMySlimBlock traceFrom = null, bool notify = true)
        {
            UnhookEvents();
            ResetCompute();

            MyCubeBlock traceFromBlock = traceFrom?.FatBlock as MyCubeBlock;
            bool tracebackPath = false; // TODO: make this feature work? currently it struggles with sorters which is where it's needed

            #region collect conveyor data
            //using(new DevProfiler("conveyor data", 2000))
            {
                if(traceFromBlock != null)
                {
                    BData_Base data = LiveData.Get<BData_Base>(traceFromBlock.BlockDefinition);
                    if(data != null && (data.Has & BlockHas.ConveyorSupport) != 0 && data.ConveyorPorts != null)
                    {
                        // ignore output ports along the way to allow identifying weird sorter setups
                        tracebackPath = true;
                    }
                }

                MyObjectBuilderType cubeBlockType = typeof(MyObjectBuilder_CubeBlock);

                foreach(MyCubeGrid grid in grids)
                {
                    foreach(MyCubeBlock block in grid.GetFatBlocks())
                    {
                        if(block.BlockDefinition.Id.TypeId == cubeBlockType)
                            continue; // easily skip a lot of irrelevant blocks

                        MyCubeBlockDefinition blockDef = block.BlockDefinition;
                        BData_Base data = LiveData.Get<BData_Base>(blockDef);
                        if(data == null)
                            continue;

                        if((data.Has & BlockHas.ConveyorSupport) == 0 || data.ConveyorPorts == null)
                            continue;

                        Conveyor conveyor = new Conveyor()
                        {
                            Data = data,
                        };
                        TempConveyorData[block] = conveyor;

                        for(int p = 0; p < data.ConveyorPorts.Count; p++)
                        {
                            ConveyorInfo port = data.ConveyorPorts[p];
                            PortPos portPos = port.TransformToGrid(block.SlimBlock);
                            PortPos expectedPortPos = new PortPos()
                            {
                                Position = portPos.Position + Base6Directions.GetIntVector(portPos.Direction),
                                Direction = Base6Directions.GetOppositeDirection(portPos.Direction),
                            };

                            IMySlimBlock otherSlim = block.CubeGrid.GetCubeBlock(expectedPortPos.Position);
                            if(otherSlim == block.SlimBlock || otherSlim?.FatBlock == null)
                                continue;

                            BData_Base otherData = LiveData.Get<BData_Base>((MyCubeBlockDefinition)otherSlim.BlockDefinition);
                            if(otherData == null || (otherData.Has & BlockHas.ConveyorSupport) == 0 || otherData.ConveyorPorts == null)
                                continue;

                            bool portSmall = (port.Flags & ConveyorFlags.Small) != 0;
                            bool portIn = (port.Flags & ConveyorFlags.In) != 0;
                            bool portOut = (port.Flags & ConveyorFlags.Out) != 0;

                            MyCubeBlock otherBlock = (MyCubeBlock)otherSlim.FatBlock;

                            // find the one port on the neighbouring block
                            for(int o = 0; o < otherData.ConveyorPorts.Count; o++)
                            {
                                ConveyorInfo otherPort = otherData.ConveyorPorts[o];
                                bool otherPortSmall = (otherPort.Flags & ConveyorFlags.Small) != 0;
                                if(otherPortSmall != portSmall)
                                    continue;

                                PortPos otherPortPos = otherPort.TransformToGrid(otherSlim);
                                if(expectedPortPos.Direction != otherPortPos.Direction || expectedPortPos.Position != otherPortPos.Position)
                                    continue;

                                bool otherPortIn = (otherPort.Flags & ConveyorFlags.In) != 0;
                                bool otherPortOut = (otherPort.Flags & ConveyorFlags.Out) != 0;

                                if((portOut && otherPortOut) || (portIn && otherPortIn))
                                    continue; // same direction so they can't connect

                                conveyor.Connected.Add(new ConnectedPort()
                                {
                                    PortIndex = p,
                                    PortGridLocalPos = (portPos.Position * block.CubeGrid.GridSize) + (Base6Directions.GetVector(portPos.Direction) * block.CubeGrid.GridSizeHalf),
                                    OtherBlock = otherBlock,
                                });
                                break;
                            }
                        }
                    }
                }
            }
            #endregion

            try
            {
                if(TempConveyorData.Count == 0)
                {
                    if(notify)
                        MyAPIGateway.Utilities.ShowNotification($"{NotifyPrefix}0 networks found");
                    return false;
                }

                HookEvents(grids);

                #region Pathfinding
                SetNetworkIdx(0);

                //using(new DevProfiler("pathing", 2000))
                {
                    //if(traceFrom != null)
                    //{
                    //    Conveyor traceFromConveyor;
                    //    if(tracebackPath && traceFromBlock != null && TempConveyorData.TryGetValue(traceFromBlock, out traceFromConveyor))
                    //    {
                    //        TempCheckedBlocks.Add(traceFromBlock);
                    //
                    //        SelectGrid(traceFrom.CubeGrid);
                    //
                    //        if(PathfindFromConveyor(traceFromBlock, traceFromConveyor))
                    //        {
                    //            SetNetworkIdx(NetworkIndex + 1);
                    //        }
                    //
                    //        CurrentGridRender.Dots.Add(new RenderDot()
                    //        {
                    //            Color = ConveyorNetworkRender.TracebackColor,
                    //            LocalPos = Vector3.Transform(traceFromConveyor.Data.ConveyorVisCenter, traceFromBlock.PositionComp.LocalMatrixRef),
                    //            Flags = RenderFlags.Pulse,
                    //        });
                    //    }
                    //    else
                    //    {
                    //        tracebackPath = false;
                    //    }
                    //}

                    foreach(KeyValuePair<MyCubeBlock, Conveyor> kv in TempConveyorData)
                    {
                        MyCubeBlock block = kv.Key;
                        Conveyor conveyor = kv.Value;

                        if(TempCheckedBlocks.Add(block))
                        {
                            if(CurrentGridRender?.Grid != block.CubeGrid)
                                SelectGrid(block.CubeGrid);

                            if(PathfindFromConveyor(block, conveyor))
                            {
                                SetNetworkIdx(NetworkIndex + 1);
                            }
                        }
                    }
                }
                #endregion

                if(notify)
                {
                    string unit = (NetworkIndex == 1 ? "network" : "networks");
                    if(tracebackPath)
                        MyAPIGateway.Utilities.ShowNotification($"{NotifyPrefix}Traceback from aimed block and {NetworkIndex} {unit}", 4000);
                    else
                        MyAPIGateway.Utilities.ShowNotification($"{NotifyPrefix}Showing {NetworkIndex} {unit}", 4000);
                }

                return true;
            }
            finally
            {
                ResetCompute();
            }
        }

        void SelectGrid(IMyCubeGrid grid)
        {
            if(CurrentGridRender == null || CurrentGridRender.Grid != grid)
            {
                if(!Render.RenderGrids.TryGetValue(grid, out CurrentGridRender))
                {
                    CurrentGridRender = new GridRender(grid);
                    Render.RenderGrids[grid] = CurrentGridRender;
                }
            }
        }

        void SetNetworkIdx(int index)
        {
            NetworkIndex = index;
            NetworkColor = ConveyorNetworkRender.NetworkColors[NetworkIndex % ConveyorNetworkRender.NetworkColors.Length];
        }

        bool PathfindFromConveyor(MyCubeBlock block, Conveyor conveyor)
        {
            bool hasConnections = conveyor.Connected.Count > 0;
            bool hasLargePorts = (conveyor.Data.Has & BlockHas.LargeConveyorPorts) != 0;
            bool drawDeadEnd = false;

            #region compute center where all lines link to from ports
            Vector3 linkCenter = Vector3.Transform(conveyor.Data.ConveyorVisCenter, block.PositionComp.LocalMatrixRef);

            if(hasConnections)
            {
                if(conveyor.Connected.Count == 1)
                {
                    // leave linkCenter to block center for a single connected port

                    if(conveyor.Data.ConveyorPorts.Count == 1)
                        drawDeadEnd = true;
                }
                else
                {
                    // NOTE: if I change linkCenter I'd have to also pass it for RenderLink

                    // makes it look more interesting with more diagonals and such but harder to read
                    //linkCenter = Vector3.Zero;
                    //foreach(ConnectedPort cp in conveyor.Connected)
                    //{
                    //    linkCenter += cp.PortGridLocalPos;
                    //}
                    //linkCenter /= conveyor.Connected.Count;

                    // average connected ports from their cell center instead of port position
                    //linkCenter = Vector3.Zero;
                    //foreach(ConnectedPort cp in conveyor.Connected)
                    //{
                    //    var port = conveyor.Data.ConveyorPorts[cp.PortIndex];
                    //    linkCenter += port.TransformToGrid(block.SlimBlock).Position;
                    //}
                    //linkCenter = (linkCenter * block.CubeGrid.GridSize) / conveyor.Connected.Count;
                }
            }
            #endregion

            bool functional = block.IsFunctional;
            Color lineColor = (functional ? NetworkColor : ConveyorNetworkRender.BrokenColor);

            #region render connected ports to center
            foreach(ConnectedPort cp in conveyor.Connected)
            {
                ConveyorInfo port = conveyor.Data.ConveyorPorts[cp.PortIndex];

                bool isSmall = (port.Flags & ConveyorFlags.Small) != 0;

                RenderFlags flags = RenderFlags.None;
                if(!functional) flags |= RenderFlags.Pulse;
                if(!hasLargePorts) flags |= RenderFlags.Small;

                CurrentGridRender.Lines.Add(new RenderLine()
                {
                    Color = lineColor,
                    LocalFrom = linkCenter,
                    LocalTo = cp.PortGridLocalPos,
                    Length = Vector3.Distance(linkCenter, cp.PortGridLocalPos),
                    Flags = flags
                });

                bool portIn = (port.Flags & ConveyorFlags.In) != 0;
                bool portOut = (port.Flags & ConveyorFlags.Out) != 0;
                if(portIn || portOut)
                {
                    PortPos portPos = port.TransformToGrid(block.SlimBlock);
                    Base6Directions.Direction dir = portPos.Direction;
                    if(portIn)
                        dir = Base6Directions.GetOppositeDirection(dir);

                    CurrentGridRender.DirectionalLines.Add(new RenderDirectional()
                    {
                        Color = lineColor,
                        LocalPos = cp.PortGridLocalPos,
                        Dir = dir,
                        Flags = flags,
                    });
                }

                if(functional && TempCheckedBlocks.Add(cp.OtherBlock))
                {
                    PathfindFromConveyor(cp.OtherBlock, TempConveyorData[cp.OtherBlock]);
                }
            }
            #endregion

            #region get connected block (connector/rotors/pistons/etc)
            bool canLinkGrids = false;
            MyCubeBlock connected = null;

            {
                var connector = block as IMyShipConnector;
                if(connector != null)
                {
                    canLinkGrids = true;
                    if(connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
                        connected = connector.OtherConnector as MyCubeBlock;
                }
            }
            {
                var mechBase = block as IMyMechanicalConnectionBlock;
                if(mechBase != null)
                {
                    canLinkGrids = true;
                    connected = mechBase.Top as MyCubeBlock;
                }
            }
            {
                var mechTop = block as IMyAttachableTopBlock;
                if(mechTop != null)
                {
                    canLinkGrids = true;
                    connected = mechTop.Base as MyCubeBlock;
                }
            }

            if(connected != null)
            {
                Conveyor connectedConveyor = TempConveyorData.GetValueOrDefault(connected, null);
                if(connectedConveyor != null)
                {
                    hasConnections = true;

                    #region scan connected grid
                    if(TempCheckedBlocks.Add(connected))
                    {
                        bool otherHasLargePorts = (connectedConveyor.Data.Has & BlockHas.LargeConveyorPorts) != 0;

                        RenderFlags flags = RenderFlags.None;
                        if(!functional) flags |= RenderFlags.Pulse;
                        if(!(hasLargePorts && otherHasLargePorts)) flags |= RenderFlags.Small;

                        Render.GridLinks.Add(new RenderLink()
                        {
                            BlockA = block,
                            BlockB = connected,
                            DataA = conveyor.Data,
                            DataB = connectedConveyor.Data,
                            Length = (float)Vector3D.Distance(block.WorldMatrix.Translation, connected.WorldMatrix.Translation),
                            Color = lineColor,
                            Flags = flags,
                        });

                        if(functional)
                        {
                            SelectGrid(connected.CubeGrid);
                            PathfindFromConveyor(connected, connectedConveyor);
                            SelectGrid(block.CubeGrid);
                        }
                    }
                    #endregion
                }
            }

            if(canLinkGrids) // && connected == null)
            {
                CurrentGridRender.Dots.Add(new RenderDot()
                {
                    Color = functional ? ConveyorNetworkRender.ConnectableColor : ConveyorNetworkRender.BrokenColor,
                    LocalPos = linkCenter,
                    Flags = hasLargePorts ? RenderFlags.None : RenderFlags.Small,
                });
            }

            if(canLinkGrids)
                drawDeadEnd = false;
            #endregion

            #region render isolated blocks
            if(!hasConnections)
            {
                foreach(ConveyorInfo port in conveyor.Data.ConveyorPorts)
                {
                    Vector3 portLocalPos = port.GetGridLocalPosition(block);

                    bool isSmall = (port.Flags & ConveyorFlags.Small) != 0;

                    RenderFlags flags = RenderFlags.Pulse;
                    if(isSmall)
                        flags |= RenderFlags.Small;

                    CurrentGridRender.Lines.Add(new RenderLine()
                    {
                        Color = ConveyorNetworkRender.IsolatedColor,
                        LocalFrom = linkCenter,
                        LocalTo = portLocalPos,
                        Length = Vector3.Distance(linkCenter, portLocalPos),
                        Flags = flags,
                    });
                }
            }
            #endregion

            #region render box per inventory
            if(block.HasInventory)
            {
                RenderFlags flags = RenderFlags.None;
                if(!hasLargePorts) flags |= RenderFlags.Small;

                float boxSize = (block.CubeGrid.GridSizeEnum == MyCubeSize.Large ? ConveyorNetworkRender.BoxSizeLG : ConveyorNetworkRender.BoxSizeSG);

                int inventories = Math.Min(block.InventoryCount, 5); // arbitrary limit

                Vector3 dir = block.PositionComp.LocalMatrixRef.Down;
                Vector3 dirStep = dir * (boxSize + ConveyorNetworkRender.BoxSpacing);
                Vector3 startPos = linkCenter - dirStep * ((inventories - 1) * 0.5f);

                Color color = (hasConnections ? lineColor : ConveyorNetworkRender.IsolatedColor) * ConveyorNetworkRender.InventoryBoxOpacity;

                for(int i = 0; i < inventories; i++)
                {
                    Vector3 pos = startPos + dirStep * i;

                    CurrentGridRender.Boxes.Add(new RenderBox()
                    {
                        Color = color,
                        LocalPos = pos,
                        Flags = flags,
                    });
                }
            }
            #endregion

            #region render single port dead-end
            if(drawDeadEnd)
            {
                RenderFlags flags = RenderFlags.None;
                //if(!functional) flags |= RenderFlags.Pulse;
                if(!hasLargePorts) flags |= RenderFlags.Small;

                CurrentGridRender.Dots.Add(new RenderDot()
                {
                    Color = lineColor,
                    LocalPos = linkCenter,
                    Flags = flags,
                });
            }
            #endregion

            return hasConnections;
        }
    }
}
