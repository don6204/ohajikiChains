#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <d3d12sdklayers.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <vector>
#include <string>
#include <stdexcept>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <cstdio>
using Microsoft::WRL::ComPtr;
namespace {
thread_local std::string lastError;
void Check(HRESULT result, const char* where) {
    if (FAILED(result)) { char code[32]; sprintf_s(code, " (HRESULT 0x%08X)", static_cast<unsigned>(result)); throw std::runtime_error(std::string(where) + code); }
}
D3D12_HEAP_PROPERTIES Heap(D3D12_HEAP_TYPE type) { D3D12_HEAP_PROPERTIES p{}; p.Type=type; p.CreationNodeMask=p.VisibleNodeMask=1; return p; }
D3D12_RESOURCE_DESC Buffer(UINT64 size) { D3D12_RESOURCE_DESC d{}; d.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER; d.Width=size; d.Height=1; d.DepthOrArraySize=1; d.MipLevels=1; d.SampleDesc.Count=1; d.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR; return d; }
void Barrier(ID3D12GraphicsCommandList* list, ID3D12Resource* resource, D3D12_RESOURCE_STATES from, D3D12_RESOURCE_STATES to) {
    D3D12_RESOURCE_BARRIER b{}; b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION; b.Transition.pResource=resource; b.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES; b.Transition.StateBefore=from; b.Transition.StateAfter=to; list->ResourceBarrier(1,&b);
}
const char* Shader=R"(
cbuffer Draw : register(b0) { float4 rect; float4 rotationShape; float4 tint; float4 view; };
struct Vertex { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
Vertex VS(uint id:SV_VertexID) {
    float2 points[6]={float2(0,0),float2(1,0),float2(0,1),float2(0,1),float2(1,0),float2(1,1)};
    Vertex o; o.uv=points[id]; float2 p=(o.uv-.5)*rect.zw; p.x+=p.y*rotationShape.w;
    p=float2(p.x*rotationShape.x-p.y*rotationShape.y,p.x*rotationShape.y+p.y*rotationShape.x)+rect.xy;
    o.position=float4(p.x/view.x*2-1,1-p.y/view.y*2,0,1); return o;
}
Texture2D tex:register(t0); SamplerState samp:register(s0);
float4 PS(Vertex i):SV_TARGET {
    float4 c=tex.Sample(samp,i.uv)*tint;
    if(rotationShape.z>0.5) {
        float d=length(i.uv*2-1); float edge=max(fwidth(d),.002);
        c.a*=1-smoothstep(1-edge,1,d);
        if(rotationShape.z>1.5) c.a*=smoothstep(.84-edge,.84,d);
    }
    return c;
})";
struct DrawCommand { float x,y,w,h,rotation,r,g,b,a; int texture,shape; float shear; };
static_assert(sizeof(DrawCommand)==48,"Interop ABI");
class Renderer {
public:
    static constexpr UINT TextureLimit=2048;
    ComPtr<IDXGIFactory6> factory;
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
    ComPtr<IDXGISwapChain3> swap;
    ComPtr<ID3D12CommandAllocator> allocator;
    ComPtr<ID3D12GraphicsCommandList> list;
    ComPtr<ID3D12DescriptorHeap> rtvHeap,srvHeap;
    ComPtr<ID3D12RootSignature> root;
    ComPtr<ID3D12PipelineState> pipeline;
    ComPtr<ID3D12Fence> fence;
    ComPtr<ID3D12Resource> targets[2];
    std::vector<ComPtr<ID3D12Resource>> textures;
    HANDLE eventHandle=nullptr;
    UINT64 fenceValue=0;
    UINT width=1,height=1,rtvStep=0,srvStep=0,lastBuffer=0;
    bool rendered=false;
    bool debugEnabled=false;
    std::string adapterName;
    ~Renderer() { try { if(queue && fence && eventHandle) Wait(); } catch(...){} if(eventHandle) CloseHandle(eventHandle); }
    void Wait() {
        Check(queue->Signal(fence.Get(),++fenceValue),"Queue::Signal");
        if(fence->GetCompletedValue()<fenceValue) {
            Check(fence->SetEventOnCompletion(fenceValue,eventHandle),"Fence::SetEventOnCompletion");
            if(WaitForSingleObject(eventHandle,10000)!=WAIT_OBJECT_0) throw std::runtime_error("GPU fence timeout / device removed");
        }
    }
    void BeginCommands() { Check(allocator->Reset(),"Allocator::Reset"); Check(list->Reset(allocator.Get(),nullptr),"List::Reset"); }
    void Submit() { Check(list->Close(),"List::Close"); ID3D12CommandList* lists[]={list.Get()}; queue->ExecuteCommandLists(1,lists); Wait(); }
    void Targets() {
        auto handle=rtvHeap->GetCPUDescriptorHandleForHeapStart();
        for(UINT i=0;i<2;++i) { Check(swap->GetBuffer(i,IID_PPV_ARGS(&targets[i])),"SwapChain::GetBuffer"); device->CreateRenderTargetView(targets[i].Get(),nullptr,handle); handle.ptr+=rtvStep; }
    }
    void Init(HWND hwnd,UINT w,UINT h,bool debug) {
        width=std::max(1u,w);height=std::max(1u,h);
        if(debug) { ComPtr<ID3D12Debug> layer; if(SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&layer)))) { layer->EnableDebugLayer();debugEnabled=true; } }
        Check(CreateDXGIFactory2(0,IID_PPV_ARGS(&factory)),"CreateDXGIFactory2");
        ComPtr<IDXGIAdapter1> adapter;
        for(UINT i=0;factory->EnumAdapterByGpuPreference(i,DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE,IID_PPV_ARGS(adapter.ReleaseAndGetAddressOf()))!=DXGI_ERROR_NOT_FOUND;++i) {
            DXGI_ADAPTER_DESC1 desc{};adapter->GetDesc1(&desc);
            if(!(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE) && SUCCEEDED(D3D12CreateDevice(adapter.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device)))) break;
        }
        if(!device) { Check(factory->EnumWarpAdapter(IID_PPV_ARGS(adapter.ReleaseAndGetAddressOf())),"EnumWarpAdapter"); Check(D3D12CreateDevice(adapter.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device)),"D3D12CreateDevice"); }
        DXGI_ADAPTER_DESC1 desc{}; adapter->GetDesc1(&desc);
        int bytes=WideCharToMultiByte(CP_UTF8,0,desc.Description,-1,nullptr,0,nullptr,nullptr);
        std::vector<char> name(bytes);WideCharToMultiByte(CP_UTF8,0,desc.Description,-1,name.data(),bytes,nullptr,nullptr);adapterName=name.data();
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;Check(device->CreateCommandQueue(&q,IID_PPV_ARGS(&queue)),"CreateCommandQueue");
        DXGI_SWAP_CHAIN_DESC1 sd{};sd.Width=width;sd.Height=height;sd.Format=DXGI_FORMAT_R8G8B8A8_UNORM;sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;sd.BufferCount=2;sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
        ComPtr<IDXGISwapChain1> chain;Check(factory->CreateSwapChainForHwnd(queue.Get(),hwnd,&sd,nullptr,nullptr,&chain),"CreateSwapChainForHwnd");Check(chain.As(&swap),"SwapChain3");
        factory->MakeWindowAssociation(hwnd,DXGI_MWA_NO_ALT_ENTER);
        D3D12_DESCRIPTOR_HEAP_DESC hd{};hd.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;hd.NumDescriptors=2;Check(device->CreateDescriptorHeap(&hd,IID_PPV_ARGS(&rtvHeap)),"Create RTV heap");rtvStep=device->GetDescriptorHandleIncrementSize(hd.Type);
        hd.Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;hd.NumDescriptors=TextureLimit;hd.Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;Check(device->CreateDescriptorHeap(&hd,IID_PPV_ARGS(&srvHeap)),"Create SRV heap");srvStep=device->GetDescriptorHandleIncrementSize(hd.Type);
        Targets();
        Check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&allocator)),"CreateCommandAllocator");
        Check(device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,allocator.Get(),nullptr,IID_PPV_ARGS(&list)),"CreateCommandList");Check(list->Close(),"Close initial list");
        Check(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)),"CreateFence");eventHandle=CreateEventW(nullptr,FALSE,FALSE,nullptr);if(!eventHandle)throw std::runtime_error("CreateEvent failed");
        D3D12_DESCRIPTOR_RANGE range{};range.RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV;range.NumDescriptors=1;range.BaseShaderRegister=0;
        D3D12_ROOT_PARAMETER params[2]{};params[0].ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;params[0].Constants.Num32BitValues=16;params[0].ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL;
        params[1].ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;params[1].DescriptorTable.NumDescriptorRanges=1;params[1].DescriptorTable.pDescriptorRanges=&range;params[1].ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
        D3D12_STATIC_SAMPLER_DESC sampler{};sampler.Filter=D3D12_FILTER_MIN_MAG_MIP_LINEAR;sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D12_TEXTURE_ADDRESS_MODE_CLAMP;sampler.MaxLOD=D3D12_FLOAT32_MAX;sampler.ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;sampler.ComparisonFunc=D3D12_COMPARISON_FUNC_ALWAYS;
        D3D12_ROOT_SIGNATURE_DESC rd{};rd.NumParameters=2;rd.pParameters=params;rd.NumStaticSamplers=1;rd.pStaticSamplers=&sampler;rd.Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
        ComPtr<ID3DBlob> blob,errors;Check(D3D12SerializeRootSignature(&rd,D3D_ROOT_SIGNATURE_VERSION_1,&blob,&errors),"SerializeRootSignature");Check(device->CreateRootSignature(0,blob->GetBufferPointer(),blob->GetBufferSize(),IID_PPV_ARGS(&root)),"CreateRootSignature");
        ComPtr<ID3DBlob> vs,ps;
        auto compile=[&](const char* entry,const char* profile,ComPtr<ID3DBlob>& output) { HRESULT hr=D3DCompile(Shader,strlen(Shader),"Ohajiki.hlsl",nullptr,nullptr,entry,profile,D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&output,&errors);if(FAILED(hr)&&errors)throw std::runtime_error(static_cast<char*>(errors->GetBufferPointer()));Check(hr,"D3DCompile");};
        compile("VS","vs_5_0",vs);compile("PS","ps_5_0",ps);
        D3D12_GRAPHICS_PIPELINE_STATE_DESC pd{};pd.pRootSignature=root.Get();pd.VS={vs->GetBufferPointer(),vs->GetBufferSize()};pd.PS={ps->GetBufferPointer(),ps->GetBufferSize()};
        auto& blend=pd.BlendState.RenderTarget[0];blend.BlendEnable=TRUE;blend.SrcBlend=D3D12_BLEND_SRC_ALPHA;blend.DestBlend=D3D12_BLEND_INV_SRC_ALPHA;blend.BlendOp=D3D12_BLEND_OP_ADD;blend.SrcBlendAlpha=D3D12_BLEND_ONE;blend.DestBlendAlpha=D3D12_BLEND_INV_SRC_ALPHA;blend.BlendOpAlpha=D3D12_BLEND_OP_ADD;blend.RenderTargetWriteMask=D3D12_COLOR_WRITE_ENABLE_ALL;
        pd.SampleMask=UINT_MAX;pd.RasterizerState.FillMode=D3D12_FILL_MODE_SOLID;pd.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;pd.RasterizerState.DepthClipEnable=TRUE;pd.DepthStencilState.DepthEnable=FALSE;pd.DepthStencilState.StencilEnable=FALSE;pd.PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;pd.NumRenderTargets=1;pd.RTVFormats[0]=sd.Format;pd.SampleDesc.Count=1;
        Check(device->CreateGraphicsPipelineState(&pd,IID_PPV_ARGS(&pipeline)),"CreateGraphicsPipelineState");
        const unsigned char white[]={255,255,255,255}; SetTexture(-1,white,1,1);
    }
    int SetTexture(int id,const unsigned char* rgba,UINT w,UINT h) {
        if(!rgba||!w||!h||w>16384||h>16384)throw std::runtime_error("Invalid texture size");
        if(id<0) { for(UINT i=1;i<textures.size();++i)if(!textures[i]){id=static_cast<int>(i);break;} if(id<0)id=static_cast<int>(textures.size()); }
        if(id>=static_cast<int>(TextureLimit))throw std::runtime_error("Texture limit reached (2048)");
        Wait();
        D3D12_RESOURCE_DESC td{};td.Dimension=D3D12_RESOURCE_DIMENSION_TEXTURE2D;td.Width=w;td.Height=h;td.DepthOrArraySize=1;td.MipLevels=1;td.Format=DXGI_FORMAT_R8G8B8A8_UNORM;td.SampleDesc.Count=1;
        auto hp=Heap(D3D12_HEAP_TYPE_DEFAULT);ComPtr<ID3D12Resource> tex;Check(device->CreateCommittedResource(&hp,D3D12_HEAP_FLAG_NONE,&td,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&tex)),"Create texture");
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT foot{};UINT64 bytes=0;device->GetCopyableFootprints(&td,0,1,0,&foot,nullptr,nullptr,&bytes);
        auto up=Heap(D3D12_HEAP_TYPE_UPLOAD);auto bd=Buffer(bytes);ComPtr<ID3D12Resource> upload;Check(device->CreateCommittedResource(&up,D3D12_HEAP_FLAG_NONE,&bd,D3D12_RESOURCE_STATE_GENERIC_READ,nullptr,IID_PPV_ARGS(&upload)),"Create texture upload");
        unsigned char* mapped=nullptr;D3D12_RANGE empty{0,0};Check(upload->Map(0,&empty,reinterpret_cast<void**>(&mapped)),"Map upload");for(UINT y=0;y<h;++y)memcpy(mapped+foot.Offset+y*foot.Footprint.RowPitch,rgba+static_cast<size_t>(y)*w*4,static_cast<size_t>(w)*4);upload->Unmap(0,nullptr);
        BeginCommands();D3D12_TEXTURE_COPY_LOCATION src{},dst{};src.pResource=upload.Get();src.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;src.PlacedFootprint=foot;dst.pResource=tex.Get();dst.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;list->CopyTextureRegion(&dst,0,0,0,&src,nullptr);Barrier(list.Get(),tex.Get(),D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);Submit();
        if(id>=static_cast<int>(textures.size()))textures.resize(id+1);textures[id]=tex;
        D3D12_SHADER_RESOURCE_VIEW_DESC srv{};srv.Format=td.Format;srv.ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D;srv.Shader4ComponentMapping=D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;srv.Texture2D.MipLevels=1;
        auto handle=srvHeap->GetCPUDescriptorHandleForHeapStart();handle.ptr+=static_cast<SIZE_T>(id)*srvStep;device->CreateShaderResourceView(tex.Get(),&srv,handle);return id;
    }
    void Resize(UINT w,UINT h) { if(!w||!h|| (w==width&&h==height))return;Wait();for(auto& t:targets)t.Reset();Check(swap->ResizeBuffers(2,w,h,DXGI_FORMAT_R8G8B8A8_UNORM,0),"ResizeBuffers");width=w;height=h;Targets();rendered=false; }
    void Render(const DrawCommand* draws,int count,float viewW,float viewH) {
        if(viewW<=0||viewH<=0||count<0||count>100000)throw std::runtime_error("Invalid frame");
        BeginCommands();lastBuffer=swap->GetCurrentBackBufferIndex();auto target=targets[lastBuffer].Get();Barrier(list.Get(),target,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
        auto rtv=rtvHeap->GetCPUDescriptorHandleForHeapStart();rtv.ptr+=static_cast<SIZE_T>(lastBuffer)*rtvStep;float clear[]={.075f,.085f,.10f,1};list->ClearRenderTargetView(rtv,clear,0,nullptr);list->OMSetRenderTargets(1,&rtv,FALSE,nullptr);
        float scale=std::min(width/viewW,height/viewH);float vx=(width-viewW*scale)*.5f,vy=(height-viewH*scale)*.5f;D3D12_VIEWPORT viewport{vx,vy,viewW*scale,viewH*scale,0,1};D3D12_RECT scissor{static_cast<LONG>(vx),static_cast<LONG>(vy),static_cast<LONG>(vx+viewW*scale),static_cast<LONG>(vy+viewH*scale)};list->RSSetViewports(1,&viewport);list->RSSetScissorRects(1,&scissor);
        list->SetGraphicsRootSignature(root.Get());list->SetPipelineState(pipeline.Get());ID3D12DescriptorHeap* heaps[]={srvHeap.Get()};list->SetDescriptorHeaps(1,heaps);list->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        for(int i=0;i<count;++i) {
            const auto& d=draws[i];if(d.a<=0||d.w==0||d.h==0)continue;
            float rad=d.rotation*3.14159265359f/180;float values[16]={d.x,d.y,d.w,d.h,cosf(rad),sinf(rad),static_cast<float>(d.shape),d.shear,d.r,d.g,d.b,d.a,viewW,viewH,0,0};
            list->SetGraphicsRoot32BitConstants(0,16,values,0);int t=d.texture;if(t<0||t>=static_cast<int>(textures.size())||!textures[t])t=0;
            auto srv=srvHeap->GetGPUDescriptorHandleForHeapStart();srv.ptr+=static_cast<UINT64>(t)*srvStep;list->SetGraphicsRootDescriptorTable(1,srv);list->DrawInstanced(6,1,0,0);
        }
        Barrier(list.Get(),target,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);Submit();Check(swap->Present(1,0),"Present");rendered=true;
    }
    void Capture(unsigned char* rgba,int bytes) {
        if(!rendered||!rgba||bytes<static_cast<UINT64>(width)*height*4)throw std::runtime_error("Capture buffer invalid");
        Wait();auto target=targets[lastBuffer].Get();auto desc=target->GetDesc();D3D12_PLACED_SUBRESOURCE_FOOTPRINT foot{};UINT64 size=0;device->GetCopyableFootprints(&desc,0,1,0,&foot,nullptr,nullptr,&size);auto hp=Heap(D3D12_HEAP_TYPE_READBACK);auto bd=Buffer(size);ComPtr<ID3D12Resource> buffer;Check(device->CreateCommittedResource(&hp,D3D12_HEAP_FLAG_NONE,&bd,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&buffer)),"Create readback");
        BeginCommands();Barrier(list.Get(),target,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_COPY_SOURCE);D3D12_TEXTURE_COPY_LOCATION src{},dst{};src.pResource=target;src.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;dst.pResource=buffer.Get();dst.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;dst.PlacedFootprint=foot;list->CopyTextureRegion(&dst,0,0,0,&src,nullptr);Barrier(list.Get(),target,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_PRESENT);Submit();
        unsigned char* data=nullptr;D3D12_RANGE read{0,static_cast<SIZE_T>(size)};Check(buffer->Map(0,&read,reinterpret_cast<void**>(&data)),"Map readback");for(UINT y=0;y<height;++y)memcpy(rgba+static_cast<size_t>(y)*width*4,data+foot.Offset+y*foot.Footprint.RowPitch,static_cast<size_t>(width)*4);D3D12_RANGE noWrite{0,0};buffer->Unmap(0,&noWrite);
    }
};
}
#define API extern "C" __declspec(dllexport)
API const char* __cdecl GE_LastError(){return lastError.c_str();}
API void* __cdecl GE_Create(HWND hwnd,int w,int h,int debug) { Renderer* r=new Renderer();try{r->Init(hwnd,w,h,debug!=0);return r;}catch(const std::exception& e){lastError=e.what();delete r;return nullptr;} }
API void __cdecl GE_Destroy(void* p){delete static_cast<Renderer*>(p);}
API const char* __cdecl GE_Adapter(void* p){return static_cast<Renderer*>(p)->adapterName.c_str();}
API int __cdecl GE_Resize(void* p,int w,int h){try{static_cast<Renderer*>(p)->Resize(w,h);return 0;}catch(const std::exception& e){lastError=e.what();return -1;}}
API int __cdecl GE_Render(void* p,const DrawCommand* d,int n,float w,float h){try{static_cast<Renderer*>(p)->Render(d,n,w,h);return 0;}catch(const std::exception& e){lastError=e.what();return -1;}}
API int __cdecl GE_Texture(void* p,int id,const unsigned char* bytes,int w,int h){try{return static_cast<Renderer*>(p)->SetTexture(id,bytes,w,h);}catch(const std::exception& e){lastError=e.what();return -1;}}
API int __cdecl GE_ReleaseTexture(void* p,int id){try{auto r=static_cast<Renderer*>(p);r->Wait();if(id>0&&id<static_cast<int>(r->textures.size()))r->textures[id].Reset();return 0;}catch(const std::exception& e){lastError=e.what();return -1;}}
API int __cdecl GE_Capture(void* p,unsigned char* bytes,int n){try{static_cast<Renderer*>(p)->Capture(bytes,n);return 0;}catch(const std::exception& e){lastError=e.what();return -1;}}
API int __cdecl GE_DebugErrorCount(void* p){auto r=static_cast<Renderer*>(p);if(!r->debugEnabled)return -1;ComPtr<ID3D12InfoQueue> info;if(FAILED(r->device.As(&info)))return -1;int errors=0;for(UINT64 i=0;i<info->GetNumStoredMessages();++i){SIZE_T size=0;info->GetMessage(i,nullptr,&size);std::vector<unsigned char> bytes(size);auto m=reinterpret_cast<D3D12_MESSAGE*>(bytes.data());if(SUCCEEDED(info->GetMessage(i,m,&size))&&m->Severity<=D3D12_MESSAGE_SEVERITY_ERROR)++errors;}return errors;}
