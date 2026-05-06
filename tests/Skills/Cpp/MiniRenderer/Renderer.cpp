// MiniRenderer impl. Exercises:
//   - out-of-line method definitions (Renderer::Init etc.)
//   - RAII guards inside method bodies (must NOT be parsed as functions, was bug #121)
//   - free functions at namespace scope
#include "Renderer.hpp"

namespace mini::renderer {

namespace {
// Fake RAII guard — the parser must NOT emit `lk` as a function, even though
// `ScopedLock lk(mutex);` matches the function regex syntactically.
class ScopedLock {
public:
    explicit ScopedLock(int& m) : mutex_(m) {}
    ~ScopedLock() {}
private:
    int& mutex_;
};
}

Renderer::Renderer()
    : frame_count_(0)
    , logger_(nullptr)
    , current_pass_(nullptr)
{}

Renderer::~Renderer() = default;

bool Renderer::Init()
{
    int mutex = 0;
    ScopedLock lk(mutex);   // <-- RAII guard, NOT a function decl
    AnotherGuard g(stuff);  // <-- not real but should also not be a function
    return true;
}

void Renderer::Shutdown() noexcept
{
    int mutex = 0;
    ScopedLock lk(mutex);
}

int Renderer::GetCapability() const noexcept
{
    return 1;
}

void Renderer::DrawMesh(const mini::math::Vec3& pos)
{
    (void)pos;
}

void Renderer::DrawText(const char* text, float x, float y) const noexcept
{
    (void)text; (void)x; (void)y;
}

// Free function at namespace scope — should be Kind=function, Container=mini::renderer.
int ComputeFrameBudget(int targetFps, int overheadMs)
{
    int frameMs = 1000 / targetFps;
    return frameMs - overheadMs;
}

} // namespace mini::renderer
