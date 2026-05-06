// MiniRenderer math primitives. Exercises the parser's handling of:
//   - alignas-decorated structs (was bug #119)
//   - alignas with nested parens (was a follow-up to #119)
//   - field declarations at struct scope
//   - operator overloads
//   - const/noexcept method modifiers
#pragma once

namespace mini::math {

// alignas with a simple integer
struct alignas(16) Vec3 {
    float x;
    float y;
    float z;
    float _pad;

    Vec3 operator+(const Vec3& other) const noexcept;
    Vec3 operator-(const Vec3& other) const noexcept;
    float Dot(const Vec3& other) const noexcept;
};

// alignas with a nested expression — was the "alignas(alignof(K) > alignof(V) ? ...)" case
template<typename K, typename V>
struct alignas(alignof(K) > alignof(V) ? alignof(K) : alignof(V)) Slot {
    K key;
    V value;
};

// __declspec decorator
struct __declspec(novtable) Matrix4x4 {
    float m[16];
    Matrix4x4 Identity() const;
    Matrix4x4 Multiply(const Matrix4x4& rhs) const noexcept;
};

// [[attr]] decorator
class [[nodiscard]] Quaternion {
public:
    float x, y, z, w;
    Quaternion Normalize() const noexcept;
};

} // namespace mini::math
