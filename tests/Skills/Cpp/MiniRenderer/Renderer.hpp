// MiniRenderer interface. Exercises:
//   - nested namespaces (mini::renderer)
//   - inheritance with multi-line declaration
//   - const/noexcept/override modifiers chained
//   - field declarations including pointers, arrays, templates
//   - friend declarations
#pragma once

#include "Math.hpp"
#include "EventBus.hpp"

namespace mini::renderer {

class IDevice
{
public:
    virtual ~IDevice() = default;
    virtual bool Init() = 0;
    virtual void Shutdown() noexcept = 0;
    virtual int  GetCapability() const noexcept = 0;
};

// Multi-line class header: `class Renderer : public IDevice`, `{` on next line.
class Renderer : public IDevice
{
public:
    Renderer();
    ~Renderer() override;

    bool Init() override;
    void Shutdown() noexcept override;
    int  GetCapability() const noexcept override;

    void DrawMesh(const mini::math::Vec3& pos);
    void DrawText(const char* text, float x, float y) const noexcept;

    // Function pointer field — should be Kind=field, not Kind=function.
    using LogFn = void (*)(const char*);

private:
    int          frame_count_;
    LogFn        logger_;
    const char*  current_pass_;
    mini::math::Vec3 camera_pos_;

    friend class RendererDebug;
};

} // namespace mini::renderer
