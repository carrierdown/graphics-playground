using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace VeldridTesting
{
    internal static class Program
    {
        private static GraphicsDevice GraphicsDevice;

        internal static void Main(string[] args)
        {
            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 960,
                WindowHeight = 540,
                WindowTitle = "Veldrid Tutorial"
            };
            Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);

            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(window);

            var bootstrapper = new VeldridBootstrap(GraphicsDevice);
            bootstrapper.CreateResources();
            bootstrapper.Draw(0, 0);

            while (window.Exists)
            {
                var events = window.PumpEvents();
                if (events.IsMouseDown(MouseButton.Left) && window.Exists)
                {
                    bootstrapper.Draw(events.MousePosition.X, events.MousePosition.Y);
                }
            }

            bootstrapper.DisposeResources();
        }
    }

    public class VeldridBootstrap
    {
        private GraphicsDevice GraphicsDevice;
        private DeviceBuffer ProjectionBuffer;
        private DeviceBuffer WorldBuffer;
        private CommandList CommandList;
        private DeviceBuffer VertexBuffer;
        private DeviceBuffer IndexBuffer;
        private Shader[] Shaders;
        private Pipeline Pipeline;
        private DeviceBuffer ProjMatrixBuffer;
        private ResourceSet ResourceSet;
        private ResourceLayout ResourceLayout;

        private const string VertexCode = @"
#version 450

layout(set = 0, binding = 0) uniform Projection
{
    mat4 _Proj;
};

layout(set = 0, binding = 1) uniform World
{
    mat4 _World;
};

layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 fsin_Position;
layout(location = 1) out vec4 fsin_Color;

void main()
{
    vec4 outPos = _Proj * _World * vec4(Position.x, Position.y, 0, 1);
    gl_Position = outPos;
    fsin_Position = outPos;
    fsin_Color = Color;
}";
        
        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 OutputColor;

void main()
{
    OutputColor = Color;
}";

        public VeldridBootstrap(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;
        }
        
        public void CreateResources()
        {
            ResourceFactory factory = GraphicsDevice.ResourceFactory;
            ProjectionBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            WorldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            ResourceLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("Projection", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("World", ResourceKind.UniformBuffer, ShaderStages.Vertex))
            );
            ResourceSet = factory.CreateResourceSet(
                new ResourceSetDescription(
                    ResourceLayout,
                    ProjectionBuffer,
                    WorldBuffer)
            );

            VertexPositionColor[] quadVertices =
            {
                new VertexPositionColor(new Vector2(0, 0), RgbaFloat.Red),
                new VertexPositionColor(new Vector2(200, 0), RgbaFloat.Red),
                new VertexPositionColor(new Vector2(200, 200), RgbaFloat.Red),
                new VertexPositionColor(new Vector2(0, 200), RgbaFloat.Red)
            };
            BufferDescription vbDescription = new BufferDescription(
                (uint) quadVertices.Length * VertexPositionColor.SizeInBytes,
                BufferUsage.VertexBuffer);
            VertexBuffer = factory.CreateBuffer(vbDescription);
            GraphicsDevice.UpdateBuffer(VertexBuffer, 0, quadVertices);

            ushort[] quadIndices = {0, 1, 2, 3, 0};
            BufferDescription ibDescription = new BufferDescription(
                (uint) quadIndices.Length * sizeof(ushort),
                BufferUsage.IndexBuffer);
            IndexBuffer = factory.CreateBuffer(ibDescription);
            GraphicsDevice.UpdateBuffer(IndexBuffer, 0, quadIndices);
            ProjMatrixBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

            ShaderDescription vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(VertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(FragmentCode),
                "main");

            Shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

            // Create pipeline
            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend, 
                new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual),
                new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology.TriangleStrip,
                new ShaderSetDescription(
                    vertexLayouts: new VertexLayoutDescription[] {vertexLayout},
                    shaders: Shaders),
                new[] { ResourceLayout },
                GraphicsDevice.SwapchainFramebuffer.OutputDescription
            );

            Pipeline = factory.CreateGraphicsPipeline(pipelineDescription);

            CommandList = factory.CreateCommandList();
        }

        public void Draw(float x, float y)
        {
            // Begin() must be called before commands can be issued.
            CommandList.Begin();

            // We want to render directly to the output window.
            CommandList.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            CommandList.UpdateBuffer(ProjectionBuffer, 0,
                Matrix4x4.CreateOrthographicOffCenter(
                    0, 
                    GraphicsDevice.SwapchainFramebuffer.Width, 
                    GraphicsDevice.SwapchainFramebuffer.Height, 
                    0, 
                    -1, 
                    1
            ));
//            CommandList.UpdateBuffer(WorldBuffer, 0, Matrix4x4.CreateTranslation(Vector3.Zero));
            CommandList.SetPipeline(Pipeline);
            CommandList.SetGraphicsResourceSet(0, ResourceSet);
            
            CommandList.UpdateBuffer(WorldBuffer, 0, Matrix4x4.CreateTranslation(new Vector3(x, y, 0)));
            CommandList.ClearColorTarget(0, RgbaFloat.Black);

            // Set all relevant state to draw our quad.
            CommandList.SetVertexBuffer(0, VertexBuffer);
            CommandList.SetIndexBuffer(IndexBuffer, IndexFormat.UInt16);
            // Issue a Draw command for a single instance with 4 indices.
            CommandList.DrawIndexed(
                indexCount: 5,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);

            // End() must be called before commands can be submitted for execution.
            CommandList.End();
            GraphicsDevice.SubmitCommands(CommandList);

            // Once commands have been submitted, the rendered image can be presented to the application window.
            GraphicsDevice.SwapBuffers();
        }

        public void DisposeResources()
        {
            Pipeline.Dispose();
            foreach (Shader shader in Shaders)
            {
                shader.Dispose();
            }

            CommandList.Dispose();
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
            GraphicsDevice.Dispose();
            ProjectionBuffer.Dispose();
            WorldBuffer.Dispose();
            ResourceSet.Dispose();
            ResourceLayout.Dispose();
        }
    }

    struct VertexPositionColor
    {
        public const uint SizeInBytes = 24;
        public Vector2 Position;
        public RgbaFloat Color;

        public VertexPositionColor(Vector2 position, RgbaFloat color)
        {
            Position = position;
            Color = color;
        }
    }
}