#include <opensubdiv/far/topologyDescriptor.h>
#include <opensubdiv/far/stencilTableFactory.h>
#include <memory>
#include <vector>
#include <cmath>
#include <stdexcept>
#include <string>

// Narrow C ABI: all returned memory belongs to the plan until StudioSubdivDestroy.
// No graphics objects cross this boundary; Unity owns evaluation and rendering.
using namespace OpenSubdiv;
#define API extern "C" __declspec(dllexport)
namespace {
struct Table {
    std::vector<int> offsets, indices;
    std::vector<float> weights;
    void Read(Far::StencilTable const& source) {
        offsets = source.GetOffsets();
        int count=offsets.empty()?0:offsets.back()+source.GetSizes().back();
        offsets.push_back(count);
        indices.assign(source.GetControlIndices().begin(),source.GetControlIndices().begin()+count);
        weights.assign(source.GetWeights().begin(),source.GetWeights().begin()+count);
    }
};
struct Plan {
    Table positions, attributes;
    std::vector<int> corners, values, parents, sharpEdges;
    int vertices;
};
thread_local std::string lastError;
}
API int StudioSubdivVersion() { return 1; }
API char const* StudioSubdivError() { return lastError.c_str(); }
API void StudioSubdivDestroy(Plan* plan) { delete plan; }
API Plan* StudioSubdivCreate(int vertices, int faces, int cornerCount,
    int const* counts, int const* corners, int attributeCount, int const* attributes,
    int /* reserved */, int const* /* reserved */, float const* /* reserved */,
    int level, int boundary) {
    try {
        lastError.clear();
        if(vertices < 3 || faces < 1 || !counts || !corners || !attributes ||
           attributeCount < 1 || level < 1 || level > 6 || boundary < 1 || boundary > 2)
            throw std::runtime_error("Invalid subdivision descriptor.");
        long long sum=0;
        for(int i=0;i<faces;++i) { if(counts[i]<3) throw std::runtime_error("A polygon has fewer than three corners."); sum+=counts[i]; }
        if(sum!=cornerCount) throw std::runtime_error("Polygon corner count mismatch.");
        long long outputFaces=sum;
        for(int i=1;i<level;++i) outputFaces*=4;
        if(outputFaces>500000) throw std::runtime_error("Subdivision exceeds the 500,000 polygon budget. Reduce the level.");
        for(int i=0;i<cornerCount;++i)
            if(corners[i]<0 || corners[i]>=vertices || attributes[i]<0 || attributes[i]>=attributeCount)
                throw std::runtime_error("Subdivision index out of bounds.");
        Far::TopologyDescriptor d;
        d.numVertices=vertices; d.numFaces=faces; d.numVertsPerFace=counts; d.vertIndicesPerFace=corners;
        Far::TopologyDescriptor::FVarChannel channel;
        channel.numValues=attributeCount; channel.valueIndices=attributes;
        d.numFVarChannels=1; d.fvarChannels=&channel;
        Sdc::Options scheme;
        scheme.SetVtxBoundaryInterpolation((Sdc::Options::VtxBoundaryInterpolation)boundary);
        scheme.SetFVarLinearInterpolation(Sdc::Options::FVAR_LINEAR_BOUNDARIES);
        Far::TopologyRefinerFactory<Far::TopologyDescriptor>::Options options(Sdc::SCHEME_CATMARK,scheme);
        options.validateFullTopology=true;
        std::unique_ptr<Far::TopologyRefiner> refiner(Far::TopologyRefinerFactory<Far::TopologyDescriptor>::Create(d,options));
        if(!refiner) throw std::runtime_error("OpenSubdiv rejected the cage topology.");
        Far::TopologyRefiner::UniformOptions uniform(level); uniform.fullTopologyInLastLevel=true;
        refiner->RefineUniform(uniform);
        std::unique_ptr<Plan> plan(new Plan());
        Far::StencilTableFactory::Options stencil;
        stencil.generateOffsets=true; stencil.generateIntermediateLevels=false;
        stencil.factorizeIntermediateLevels=true; stencil.maxLevel=level;
        std::unique_ptr<Far::StencilTable const> p(Far::StencilTableFactory::Create(*refiner,stencil));
        if(!p) throw std::runtime_error("OpenSubdiv position stencils are unavailable.");
        plan->positions.Read(*p);
        stencil.interpolationMode=Far::StencilTableFactory::INTERPOLATE_FACE_VARYING;
        std::unique_ptr<Far::StencilTable const> a(Far::StencilTableFactory::Create(*refiner,stencil));
        if(!a) throw std::runtime_error("OpenSubdiv face-varying stencils are unavailable.");
        plan->attributes.Read(*a);
        auto const& last=refiner->GetLevel(level); plan->vertices=last.GetNumVertices();
        for(int f=0;f<last.GetNumFaces();++f) {
            auto c=last.GetFaceVertices(f); auto v=last.GetFaceFVarValues(f);
            if(c.size()!=4) throw std::runtime_error("Expected Catmull-Clark quad output.");
            plan->corners.insert(plan->corners.end(),c.begin(),c.end());
            plan->values.insert(plan->values.end(),v.begin(),v.end());
            int parent=f;
            for(int l=level;l>0;--l) parent=refiner->GetLevel(l).GetFaceParentFace(parent);
            plan->parents.push_back(parent);
        }
        for(int e=0;e<last.GetNumEdges();++e) if(last.GetEdgeSharpness(e)>0) {
            auto edge=last.GetEdgeVertices(e); plan->sharpEdges.push_back(edge[0]); plan->sharpEdges.push_back(edge[1]);
        }
        return plan.release();
    } catch(std::exception const& error) { lastError=error.what(); return nullptr; }
      catch(...) { lastError="Unknown OpenSubdiv construction failure."; return nullptr; }
}
API int const* StudioSubdivInts(Plan const* p,int field,int* count) {
    if(!p || !count) return nullptr;
    std::vector<int> const* result=nullptr;
    switch(field) {
    case 0:result=&p->positions.offsets;break; case 1:result=&p->positions.indices;break;
    case 2:result=&p->attributes.offsets;break; case 3:result=&p->attributes.indices;break;
    case 4:result=&p->corners;break; case 5:result=&p->values;break;
    case 6:result=&p->parents;break; case 7:result=&p->sharpEdges;break;
    default:*count=0;return nullptr;
    }
    *count=(int)result->size(); return result->data();
}
API float const* StudioSubdivWeights(Plan const* p,int channel,int* count) {
    if(!p || !count) return nullptr;
    auto const& result=channel==0?p->positions.weights:p->attributes.weights;
    *count=(int)result.size(); return result.data();
}
