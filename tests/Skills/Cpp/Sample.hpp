// Test fixture for VSMCP cpp_* tools. Do NOT modify by hand — destructive
// tests copy this file to a temp location before running.
#pragma once

#include "SampleBase.hpp"

namespace VSMCP::Test {

/// A small sample class used by the C++ E2E tests. Carries enough surface to
/// exercise outline + members + inheritance + virtuals + fields.
class Sample : public SampleBase
{
public:
    Sample(int seed);
    ~Sample() override;

    int  Compute(int x) override;
    void Reset() override;
    const char* Name() const override;

    int Multiply(int a, int b) const noexcept;

private:
    int        seed_;
    int        accum_;
    const char* name_;
};

struct Point
{
    int x;
    int y;
};

enum class Color : int { Red, Green, Blue };

} // namespace VSMCP::Test
