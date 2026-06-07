using System;
using System.Runtime.InteropServices;
using Godot;
using ImGuiFSharp;

public class GodotImGuiBackend : IImGuiBackend
{
    private readonly RenderingDevice _rd;

    private readonly string _vertGlsl = @"#version 450

layout(push_constant) uniform Transform {
    vec2 scale;
    vec2 translate;
} uT;

layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;

layout(location = 0) out vec2 vUV;
layout(location = 1) out vec4 vColor;

void main() {
    vUV    = aUV;
    vColor = aColor;
    gl_Position = vec4(aPos * uT.scale + uT.translate, 0.0, 1.0);
}
";

    private readonly string _fragGlsl = @"#version 450

layout(set = 0, binding = 0) uniform sampler2D uFont;

layout(location = 0) in vec2 vUV;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 outColor;

void main() {
    outColor = vColor * texture(uFont, vUV);
}
";

    private Rid _shaderRid = default;
    private Rid _fontTexRid = default;
    private Rid _fontSampler = default;
    private Rid _uniformSet = default;
    private Rid _pipeline = default;
    private Rid _vtxBuf = default;
    private Rid _idxBuf = default;
    private int _vtxBufSize = 0;
    private int _idxBufSize = 0;
    private Rid _offTexRid = default;
    private Rid _fbRid = default;
    private long _vtxFormatId = 0L;
    private int _fbWidth;
    private int _fbHeight;

    public Rid OffscreenRdTexRid => _offTexRid;

    public GodotImGuiBackend(RenderingDevice rd, int initialWidth, int initialHeight)
    {
        _rd = rd;
        _fbWidth = Math.Max(1, initialWidth);
        _fbHeight = Math.Max(1, initialHeight);
    }

    public void Initialize()
    {
        UploadFontAtlas();
    }

    public void SetDisplaySize(float width, float height)
    {
        var w = Math.Max(1, (int)width);
        var h = Math.Max(1, (int)height);
        if (w != _fbWidth || h != _fbHeight)
        {
            _fbWidth = w;
            _fbHeight = h;
            RecreateOffscreenFramebuffer();
        }
    }

    public void NewFrame(float delta)
    {
        // No-op for backend rendering device, managed at upper level if needed
    }

    private void RecreateOffscreenFramebuffer()
    {
        if (_fbRid.IsValid)
        {
            _rd.FreeRid(_fbRid);
            _fbRid = default;
        }
        if (_offTexRid.IsValid)
        {
            _rd.FreeRid(_offTexRid);
            _offTexRid = default;
        }

        var (tex, fb) = CreateOffscreenResources(_fbWidth, _fbHeight);
        _offTexRid = tex;
        _fbRid = fb;

        // Recreate the render pipeline with the new framebuffer format
        if (_pipeline.IsValid)
        {
            _rd.FreeRid(_pipeline);
            _pipeline = default;
        }
        BuildPipeline();
    }

    private (Rid, Rid) CreateOffscreenResources(int w, int h)
    {
        var tf = new RDTextureFormat();
        tf.Format = RenderingDevice.DataFormat.R8G8B8A8Unorm;
        tf.Width = (uint)w;
        tf.Height = (uint)h;
        tf.Depth = 1;
        tf.ArrayLayers = 1;
        tf.Mipmaps = 1;
        tf.TextureType = RenderingDevice.TextureType.Type2D;
        tf.UsageBits = RenderingDevice.TextureUsageBits.ColorAttachmentBit
                     | RenderingDevice.TextureUsageBits.SamplingBit
                     | RenderingDevice.TextureUsageBits.CanCopyFromBit;

        var tex = _rd.TextureCreate(tf, new RDTextureView(), new Godot.Collections.Array<byte[]>());
        var texArr = new Godot.Collections.Array<Rid>();
        texArr.Add(tex);
        var fb = _rd.FramebufferCreate(texArr);
        return (tex, fb);
    }

    private void BuildPipeline()
    {
        var attrs = new Godot.Collections.Array<RDVertexAttribute>();
        
        var a0 = new RDVertexAttribute();
        a0.Location = 0;
        a0.Offset = 0;
        a0.Format = RenderingDevice.DataFormat.R32G32Sfloat;
        a0.Stride = 20;
        a0.Binding = 0;
        attrs.Add(a0);

        var a1 = new RDVertexAttribute();
        a1.Location = 1;
        a1.Offset = 8;
        a1.Format = RenderingDevice.DataFormat.R32G32Sfloat;
        a1.Stride = 20;
        a1.Binding = 0;
        attrs.Add(a1);

        var a2 = new RDVertexAttribute();
        a2.Location = 2;
        a2.Offset = 16;
        a2.Format = RenderingDevice.DataFormat.R8G8B8A8Unorm;
        a2.Stride = 20;
        a2.Binding = 0;
        attrs.Add(a2);

        _vtxFormatId = _rd.VertexFormatCreate(attrs);

        var blendAtt = new RDPipelineColorBlendStateAttachment();
        blendAtt.EnableBlend = true;
        blendAtt.SrcColorBlendFactor = RenderingDevice.BlendFactor.SrcAlpha;
        blendAtt.DstColorBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha;
        blendAtt.ColorBlendOp = RenderingDevice.BlendOperation.Add;
        blendAtt.SrcAlphaBlendFactor = RenderingDevice.BlendFactor.One;
        blendAtt.DstAlphaBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha;
        blendAtt.AlphaBlendOp = RenderingDevice.BlendOperation.Add;

        var blendState = new RDPipelineColorBlendState();
        blendState.Attachments = new Godot.Collections.Array<RDPipelineColorBlendStateAttachment> { blendAtt };

        var fbFormat = _rd.FramebufferGetFormat(_fbRid);
        _pipeline = _rd.RenderPipelineCreate(
            _shaderRid, fbFormat, _vtxFormatId,
            RenderingDevice.RenderPrimitive.Triangles,
            new RDPipelineRasterizationState(),
            new RDPipelineMultisampleState(),
            new RDPipelineDepthStencilState(),
            blendState);
    }

    private void EnsureVtxBuf(int needed)
    {
        if (needed > _vtxBufSize)
        {
            int sz = Math.Max(needed, Math.Max(65536, _vtxBufSize * 2));
            if (_vtxBuf.IsValid) _rd.FreeRid(_vtxBuf);
            _vtxBuf = _rd.VertexBufferCreate((uint)sz, Array.Empty<byte>());
            _vtxBufSize = sz;
        }
    }

    private void EnsureIdxBuf(int needed)
    {
        if (needed > _idxBufSize)
        {
            int sz = Math.Max(needed, Math.Max(65536, _idxBufSize * 2));
            if (_idxBuf.IsValid) _rd.FreeRid(_idxBuf);
            _idxBuf = _rd.IndexBufferCreate((uint)sz, RenderingDevice.IndexBufferFormat.Uint16, Array.Empty<byte>());
            _idxBufSize = sz;
        }
    }

    private void UploadFontAtlas()
    {
        var src = new RDShaderSource();
        src.SetStageSource(RenderingDevice.ShaderStage.Vertex, _vertGlsl);
        src.SetStageSource(RenderingDevice.ShaderStage.Fragment, _fragGlsl);
        
        var spirv = _rd.ShaderCompileSpirVFromSource(src, true);
        var vertErr = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Vertex);
        var fragErr = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Fragment);
        if (!string.IsNullOrEmpty(vertErr)) GD.PrintErr("ImGui vert: " + vertErr);
        if (!string.IsNullOrEmpty(fragErr)) GD.PrintErr("ImGui frag: " + fragErr);
        _shaderRid = _rd.ShaderCreateFromSpirV(spirv);

        var (tex, fb) = CreateOffscreenResources(_fbWidth, _fbHeight);
        _offTexRid = tex;
        _fbRid = fb;

        BuildPipeline();

        ImGuiNative.IGN_Font_Build();
        IntPtr pixelsPtr = IntPtr.Zero;
        int w = 0, h = 0;
        ImGuiNative.IGN_Font_GetTexData(ref pixelsPtr, ref w, ref h);
        
        int byteCount = w * h * 4;
        byte[] fontData = new byte[byteCount];
        Marshal.Copy(pixelsPtr, fontData, 0, byteCount);

        var ftf = new RDTextureFormat();
        ftf.Format = RenderingDevice.DataFormat.R8G8B8A8Unorm;
        ftf.Width = (uint)w;
        ftf.Height = (uint)h;
        ftf.Depth = 1;
        ftf.ArrayLayers = 1;
        ftf.Mipmaps = 1;
        ftf.TextureType = RenderingDevice.TextureType.Type2D;
        ftf.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit
                      | RenderingDevice.TextureUsageBits.CanCopyToBit;

        var initData = new Godot.Collections.Array<byte[]>();
        initData.Add(fontData);
        _fontTexRid = _rd.TextureCreate(ftf, new RDTextureView(), initData);

        var ss = new RDSamplerState();
        ss.MinFilter = RenderingDevice.SamplerFilter.Linear;
        ss.MagFilter = RenderingDevice.SamplerFilter.Linear;
        ss.RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge;
        ss.RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge;
        _fontSampler = _rd.SamplerCreate(ss);

        var uni = new RDUniform();
        uni.UniformType = RenderingDevice.UniformType.SamplerWithTexture;
        uni.Binding = 0;
        uni.AddId(_fontSampler);
        uni.AddId(_fontTexRid);
        
        var uniArr = new Godot.Collections.Array<RDUniform>();
        uniArr.Add(uni);
        _uniformSet = _rd.UniformSetCreate(uniArr, _shaderRid, 0);

        ImGuiNative.IGN_Font_SetTexID(1);
    }

    private struct DrawCommandInfo
    {
        public Rect2 Scissor;
        public Rid IdxArr;
        public Rid VtxArr;
    }

    public void Render(IntPtr drawData)
    {
        if (drawData == IntPtr.Zero || !_fbRid.IsValid) return;

        float posX = 0f, posY = 0f, sizeW = 0f, sizeH = 0f, fbScaleX = 0f, fbScaleY = 0f;
        ImGuiNative.IGN_DrawData_GetDisplayInfo(drawData, ref posX, ref posY, ref sizeW, ref sizeH, ref fbScaleX, ref fbScaleY);
        if (sizeW <= 0f || sizeH <= 0f) return;

        byte[] pushBytes = new byte[16];
        void SetF32(byte[] arr, int offset, float val)
        {
            byte[] b = BitConverter.GetBytes(val);
            Buffer.BlockCopy(b, 0, arr, offset, 4);
        }
        SetF32(pushBytes, 0, 2f / sizeW);
        SetF32(pushBytes, 4, 2f / sizeH);
        SetF32(pushBytes, 8, -1f);
        SetF32(pushBytes, 12, -1f);

        int listCount = ImGuiNative.IGN_DrawData_GetCmdListCount(drawData);
        int totalVtxCount = 0;
        int totalIdxCount = 0;

        var lists = new (int vtxCount, int idxCount, int cmdCount, IntPtr vtxPtr, IntPtr idxPtr)[listCount];
        for (int li = 0; li < listCount; li++)
        {
            int vtxCount = 0, idxCount = 0, cmdCount = 0;
            IntPtr vtxPtr = IntPtr.Zero, idxPtr = IntPtr.Zero;
            ImGuiNative.IGN_DrawData_GetCmdList(drawData, li, ref vtxCount, ref idxCount, ref vtxPtr, ref idxPtr, ref cmdCount);
            lists[li] = (vtxCount, idxCount, cmdCount, vtxPtr, idxPtr);
            totalVtxCount += vtxCount;
            totalIdxCount += idxCount;
        }

        if (totalVtxCount > 0 && totalIdxCount > 0)
        {
            int totalVtxBytes = totalVtxCount * 20;
            int totalIdxBytes = totalIdxCount * 2;
            EnsureVtxBuf(totalVtxBytes);
            EnsureIdxBuf(totalIdxBytes);

            byte[] vtxData = new byte[totalVtxBytes];
            byte[] idxData = new byte[totalIdxBytes];

            int currVtxBytes = 0;
            int currIdxBytes = 0;
            int[] listVtxOffsetInElements = new int[listCount];
            int[] listIdxOffsetInElements = new int[listCount];

            int currVtxElements = 0;
            int currIdxElements = 0;

            for (int li = 0; li < listCount; li++)
            {
                var list = lists[li];
                listVtxOffsetInElements[li] = currVtxElements;
                listIdxOffsetInElements[li] = currIdxElements;

                if (list.vtxCount > 0)
                {
                    int bytes = list.vtxCount * 20;
                    Marshal.Copy(list.vtxPtr, vtxData, currVtxBytes, bytes);
                    currVtxBytes += bytes;
                    currVtxElements += list.vtxCount;
                }

                if (list.idxCount > 0)
                {
                    int bytes = list.idxCount * 2;
                    Marshal.Copy(list.idxPtr, idxData, currIdxBytes, bytes);
                    currIdxBytes += bytes;
                    currIdxElements += list.idxCount;
                }
            }

            _rd.BufferUpdate(_vtxBuf, 0, (uint)totalVtxBytes, vtxData);
            _rd.BufferUpdate(_idxBuf, 0, (uint)totalIdxBytes, idxData);

            var bufs = new Godot.Collections.Array<Rid>();
            bufs.Add(_vtxBuf);

            var drawCommands = new System.Collections.Generic.List<DrawCommandInfo>();
            for (int li = 0; li < listCount; li++)
            {
                var list = lists[li];
                for (int ci = 0; ci < list.cmdCount; ci++)
                {
                    int elemCount = 0;
                    uint texId = 0;
                    float clipX = 0f, clipY = 0f, clipZ = 0f, clipW = 0f;
                    uint idxOffset = 0;
                    uint vtxOffset = 0;

                    ImGuiNative.IGN_DrawData_GetCmd(drawData, li, ci, ref elemCount, ref texId, ref clipX, ref clipY, ref clipZ, ref clipW, ref idxOffset, ref vtxOffset);

                    float sx = (clipX - posX) * fbScaleX;
                    float sy = (clipY - posY) * fbScaleY;
                    float sw = (clipZ - clipX) * fbScaleX;
                    float sh = (clipW - clipY) * fbScaleY;
                    var scissor = new Rect2(sx, sy, sw, sh);

                    uint absoluteIdxOffset = (uint)listIdxOffsetInElements[li] + idxOffset;
                    uint absoluteVtxOffset = (uint)listVtxOffsetInElements[li] + vtxOffset;

                    var idxArr = _rd.IndexArrayCreate(_idxBuf, absoluteIdxOffset, (uint)elemCount);
                    
                    // Create a vertex array view starting at this command's vertex offset (in bytes)
                    long vtxArrOffset = (long)(absoluteVtxOffset * 20u);
                    var cmdVtxArr = _rd.VertexArrayCreate((uint)totalVtxCount, _vtxFormatId, bufs, new long[] { vtxArrOffset });

                    drawCommands.Add(new DrawCommandInfo
                    {
                        Scissor = scissor,
                        IdxArr = idxArr,
                        VtxArr = cmdVtxArr
                    });
                }
            }

            var clearColors = new Color[] { new Color(0f, 0f, 0f, 0f) };
            var drawList = _rd.DrawListBegin(_fbRid, RenderingDevice.DrawFlags.ClearColor0, clearColors, 1f, 0u, new Rect2());

            _rd.DrawListBindRenderPipeline(drawList, _pipeline);
            _rd.DrawListSetPushConstant(drawList, pushBytes, (uint)pushBytes.Length);
            _rd.DrawListBindUniformSet(drawList, _uniformSet, 0u);

            foreach (var cmd in drawCommands)
            {
                _rd.DrawListEnableScissor(drawList, cmd.Scissor);
                _rd.DrawListBindIndexArray(drawList, cmd.IdxArr);
                _rd.DrawListBindVertexArray(drawList, cmd.VtxArr);
                _rd.DrawListDraw(drawList, true, 1u, 0u);
            }

            _rd.DrawListEnd();

            foreach (var cmd in drawCommands)
            {
                _rd.FreeRid(cmd.IdxArr);
                _rd.FreeRid(cmd.VtxArr);
            }
        }
        else
        {
            var clearColors = new Color[] { new Color(0f, 0f, 0f, 0f) };
            var drawList = _rd.DrawListBegin(_fbRid, RenderingDevice.DrawFlags.ClearColor0, clearColors, 1f, 0u, new Rect2());
            _rd.DrawListEnd();
        }
    }

    public void Destroy()
    {
        void FreeIf(Rid r)
        {
            if (r.IsValid) _rd.FreeRid(r);
        }

        FreeIf(_uniformSet);
        FreeIf(_pipeline);
        FreeIf(_fbRid);
        FreeIf(_offTexRid);
        FreeIf(_fontSampler);
        FreeIf(_fontTexRid);
        FreeIf(_shaderRid);
        FreeIf(_vtxBuf);
        FreeIf(_idxBuf);
    }
}
