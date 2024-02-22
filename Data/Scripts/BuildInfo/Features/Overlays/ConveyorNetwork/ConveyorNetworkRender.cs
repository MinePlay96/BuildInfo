﻿using System.Collections.Generic;
using Digi.BuildInfo.Utilities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Digi.BuildInfo.Features.Overlays.ConveyorNetwork
{
    public class ConveyorNetworkRender
    {
        public const double RangeOutsideShipVolume = 100;

        public static readonly Vector4[] Colors = new Vector4[]
        {
            new Vector4(1, 1, 0, 1),
            //new Vector4(1, 0.5f, 0, 1),
            new Vector4(0, 1, 1, 1),
            //new Vector4(0, 0.5f, 1, 1),
            new Vector4(0, 0, 1, 1),
            //new Vector4(0, 1, 0.5f, 1),
            new Vector4(0, 1, 0, 1),
        };

        public static readonly Color ConnectableColor = new Color(255, 0, 255);
        public static readonly Color TracebackColor = new Color(0, 155, 255) * 0.5f;
        public static readonly Color IsolatedColor = new Color(255, 0, 0);
        public static readonly Color BrokenColor = new Color(255, 80, 10);
        public static readonly Color ArrowColor = new Color(255, 0, 200);

        public const float InventoryBoxOpacity = 0.75f;
        public const double NoSmallDarken = 0.75;
        public const float BoxSizeSG = 0.16f;
        public const float BoxSizeLG = 0.6f;
        public const float BoxSpacing = 0.05f;

        readonly MyStringId MaterialLine = MyStringId.GetOrCompute("BuildInfo_Square");
        readonly MyStringId MaterialLineShadow = MyStringId.GetOrCompute("BuildInfo_ShadowedLine");
        readonly MyStringId MaterialDot = MyStringId.GetOrCompute("BuildInfo_Dot");
        readonly MyStringId MaterialDotShadow = MyStringId.GetOrCompute("BuildInfo_ShadowedDot");
        readonly MyStringId MaterialArrow = MyStringId.GetOrCompute("BuildInfo_Arrow");
        readonly MyStringId MaterialArrowShadow = MyStringId.GetOrCompute("BuildInfo_ShadowedArrow");
        readonly MyStringId MaterialSquare = MyStringId.GetOrCompute("BuildInfo_Square");

        const float DepthRatio = 0.01f;

        // port size not grid size
        const float BaseThickLarge = 0.4f;
        const float BaseThickSmall = 0.15f;

        // these are tweaked based on the textures
        const float LineThickMul = 0.16f;
        const float LineShadowThickMul = 1f;
        const float DotThickMul = 0.75f;
        const float DotShadowThickMul = 2.5f;
        const float ArrowThickMul = LineShadowThickMul * 1.5f;
        const float ArrowShadowThickMul = ArrowThickMul;

        internal readonly Dictionary<IMyCubeGrid, GridRender> RenderGrids = new Dictionary<IMyCubeGrid, GridRender>();
        internal readonly List<RenderLink> GridLinks = new List<RenderLink>();

        readonly HashSet<IMyCubeGrid> TempClosedGrids = new HashSet<IMyCubeGrid>();

        ConveyorNetworkView Handler;

        float Pulse;
        bool IgnoreSmall = false;

        public ConveyorNetworkRender(ConveyorNetworkView handler)
        {
            Handler = handler;
        }

        public void Init()
        {
        }

        public void Reset()
        {
            RenderGrids.Clear();
            GridLinks.Clear();
        }

        public bool IsValid()
        {
            BoundingSphereD sphere = BoundingSphereD.CreateInvalid();

            try
            {
                TempClosedGrids.Clear();

                foreach(GridRender gridRender in RenderGrids.Values)
                {
                    if(gridRender.Grid.Closed || gridRender.Grid.MarkedForClose)
                    {
                        TempClosedGrids.Add(gridRender.Grid);
                    }
                    else
                    {
                        sphere.Include(gridRender.Grid.WorldVolume);
                    }
                }

                foreach(IMyCubeGrid key in TempClosedGrids)
                {
                    RenderGrids.Remove(key);
                }
            }
            finally
            {
                TempClosedGrids.Clear();
            }

            if(RenderGrids.Count == 0)
                return false;

            Vector3D camPos = MyAPIGateway.Session.Camera.Position;
            double distSq = (sphere.Radius * sphere.Radius) + (RangeOutsideShipVolume * RangeOutsideShipVolume);
            if(Vector3D.DistanceSquared(camPos, sphere.Center) > distSq)
                return false;

            return true;
        }

        public void Draw()
        {
            Pulse = Utils.Pulse(0.5f, 1.5f, freq: 1.2f);

            DrawLayer(isShadow: true);
            DrawLayer(drawLines: true);
            DrawBoxes();
            DrawLayer(drawDots: true);
        }

        void DrawLayer(bool isShadow = false, bool drawLines = false, bool drawDots = false)
        {
            Vector3D camPos = MyAPIGateway.Session.Camera.Position;

            if(isShadow)
            {
                drawLines = true;
                drawDots = true;
            }

            // grid-local render objects
            foreach(GridRender gridRender in RenderGrids.Values)
            {
                MatrixD transform = gridRender.Grid.PositionComp.WorldMatrixRef;

                if(drawLines)
                {
                    MyStringId material = (isShadow ? MaterialLineShadow : MaterialLine);

                    for(int i = 0; i < gridRender.Lines.Count; i++)
                    {
                        RenderLine line = gridRender.Lines[i];
                        bool isSmallPort = (line.Flags & RenderFlags.Small) != 0;

                        float thickness = (isSmallPort ? BaseThickSmall : BaseThickLarge)
                                        * (isShadow ? LineShadowThickMul : LineThickMul)
                                        * DepthRatio;

                        if((line.Flags & RenderFlags.Pulse) != 0)
                            thickness *= Pulse;

                        Color color = Color.Black;
                        if(!isShadow)
                        {
                            color = line.Color;
                            if(IgnoreSmall && isSmallPort)
                                color = Color.Darken(color, NoSmallDarken);
                        }

                        Vector3D from = Vector3D.Transform(line.LocalFrom, transform);
                        Vector3D fromClose = camPos + ((from - camPos) * DepthRatio);
                        Vector3D to = Vector3D.Transform(line.LocalTo, transform);
                        Vector3D toClose = camPos + ((to - camPos) * DepthRatio);

                        MyTransparentGeometry.AddLineBillboard(material, color,
                            fromClose, (toClose - fromClose), 1f,
                            thickness: thickness,
                            blendType: BlendTypeEnum.PostPP);
                    }
                }

                if(drawDots)
                {
                    MyStringId material = (isShadow ? MaterialDotShadow : MaterialDot);

                    for(int i = 0; i < gridRender.Dots.Count; i++)
                    {
                        RenderDot dot = gridRender.Dots[i];
                        bool isSmallPort = (dot.Flags & RenderFlags.Small) != 0;

                        float thickness = (isSmallPort ? BaseThickSmall : BaseThickLarge)
                                        * (isShadow ? DotShadowThickMul : DotThickMul)
                                        * DepthRatio;

                        Color color = Color.Black;
                        if(!isShadow)
                        {
                            color = dot.Color;
                            if(IgnoreSmall && isSmallPort)
                                color = Color.Darken(color, NoSmallDarken);
                        }

                        if((dot.Flags & RenderFlags.Pulse) != 0)
                            thickness *= Pulse;

                        Vector3D pos = Vector3D.Transform(dot.LocalPos, transform);
                        Vector3D posClose = camPos + ((pos - camPos) * DepthRatio);

                        MyTransparentGeometry.AddPointBillboard(material, color, posClose,
                            radius: thickness * DotThickMul, angle: 0,
                            blendType: BlendTypeEnum.PostPP);
                    }
                }

                if(drawLines)
                {
                    MyStringId material = (isShadow ? MaterialArrowShadow : MaterialArrow);

                    for(int i = 0; i < gridRender.DirectionalLines.Count; i++)
                    {
                        RenderDirectional directionalLine = gridRender.DirectionalLines[i];

                        bool isSmallPort = (directionalLine.Flags & RenderFlags.Small) != 0;

                        float thickness = (isSmallPort ? BaseThickSmall : BaseThickLarge)
                                        * (isShadow ? ArrowShadowThickMul : ArrowThickMul);

                        Color color = Color.Black;
                        if(!isShadow)
                        {
                            color = directionalLine.Color;
                            if(IgnoreSmall && isSmallPort)
                                color = Color.Darken(color, NoSmallDarken);
                        }

                        if((directionalLine.Flags & RenderFlags.Pulse) != 0)
                            thickness *= Pulse;

                        Vector3D dir = transform.GetDirectionVector(directionalLine.Dir);
                        Vector3D pos = Vector3D.Transform(directionalLine.LocalPos, transform);
                        pos -= dir * thickness; // since this is a line, offset by half length so the input position is centered
                        thickness *= DepthRatio;

                        Vector3D posClose = camPos + ((pos - camPos) * DepthRatio);

                        // axis-locked square billboard
                        MyTransparentGeometry.AddLineBillboard(material, color,
                            posClose, dir, length: thickness * 2, thickness: thickness,
                            blendType: BlendTypeEnum.PostPP);
                    }
                }
            }

            // mechanical blocks connecting grids
            if(drawLines)
            {
                MyStringId material = (isShadow ? MaterialLineShadow : MaterialLine);

                foreach(RenderLink link in GridLinks)
                {
                    Vector3D from = Vector3D.Transform(link.BlockA.LocalAABB.Center, link.BlockA.WorldMatrix);
                    Vector3D fromClose = camPos + ((from - camPos) * DepthRatio);

                    Vector3D to = Vector3D.Transform(link.BlockB.LocalAABB.Center, link.BlockB.WorldMatrix);
                    Vector3D toClose = camPos + ((to - camPos) * DepthRatio);

                    bool isSmallPort = (link.Flags & RenderFlags.Small) != 0;

                    Color color = Color.Black;
                    if(!isShadow)
                    {
                        color = link.Color;
                        if(IgnoreSmall && isSmallPort)
                            color = Color.Darken(color, NoSmallDarken);
                    }

                    float thickness = (isSmallPort ? BaseThickSmall : BaseThickLarge)
                                    * (isShadow ? LineShadowThickMul : LineThickMul)
                                    * DepthRatio;

                    if((link.Flags & RenderFlags.Pulse) != 0)
                        thickness *= Pulse;

                    MyTransparentGeometry.AddLineBillboard(material, color,
                        fromClose, (toClose - fromClose), 1f,
                        thickness: thickness,
                        blendType: BlendTypeEnum.PostPP);
                }
            }
        }

        void DrawBoxes()
        {
            foreach(GridRender gridRender in RenderGrids.Values)
            {
                MatrixD transform = gridRender.Grid.PositionComp.WorldMatrixRef;

                float boxSize = (gridRender.Grid.GridSizeEnum == MyCubeSize.Large ? BoxSizeLG : BoxSizeSG);
                BoundingBoxD bb = new BoundingBoxD(new Vector3D(boxSize / -2), new Vector3D(boxSize / 2));

                MyStringId material = MaterialSquare;

                for(int i = 0; i < gridRender.Boxes.Count; i++)
                {
                    RenderBox box = gridRender.Boxes[i];

                    MatrixD boxWM = transform;
                    boxWM.Translation = Vector3D.Transform(box.LocalPos, transform);

                    float depthRatio = OverlayDrawInstance.ConvertToAlwaysOnTop(ref boxWM);

                    Color color = box.Color;
                    MySimpleObjectDraw.DrawTransparentBox(ref boxWM, ref bb, ref color, MySimpleObjectRasterizer.Solid, 1, onlyFrontFaces: true, faceMaterial: material, blendType: BlendTypeEnum.PostPP);
                }
            }
        }
    }
}