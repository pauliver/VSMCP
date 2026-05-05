// Test fixture for VSMCP cpp_* tools. Do NOT modify by hand — destructive
// tests copy this file to a temp location before running.
#pragma once

namespace VSMCP::Test {

class SampleBase
{
public:
    SampleBase() = default;
    virtual ~SampleBase() = default;

    virtual int  Compute(int x) = 0;
    virtual void Reset() = 0;
    virtual const char* Name() const = 0;

protected:
    int callCount = 0;
};

} // namespace VSMCP::Test
