// MiniRenderer event-bus primitives. Exercises:
//   - multi-line function parameter lists (was bug #117)
//   - `static` member methods
//   - templated class with virtual interface
//   - pure virtuals with `= 0`
//   - default arguments
#pragma once

#include <cstddef>

namespace mini::bus {

enum class DeliveryMode { Synchronous, Async };
using DllToken = unsigned long;
using SubscriptionHandle = unsigned long;

template<typename T>
class EventBus
{
public:
    using Handler = void (*)(const T& msg, void* userdata);

    // Multi-line declaration — Subscribe-style pattern from real-world Zengine.
    // The parser should JoinContinuationLines and emit ONE method named "Subscribe",
    // not phantom fields for each parameter line.
    static SubscriptionHandle Subscribe(Handler      fn,
                                        void*        userdata    = nullptr,
                                        DeliveryMode mode        = DeliveryMode::Synchronous,
                                        DllToken     dll         = 0) noexcept;

    static void Unsubscribe(SubscriptionHandle h) noexcept;
    static void Publish(const T& msg) noexcept;

    static std::size_t SubscriberCount() noexcept;
};

// Pure-virtual interface used by ImplementInterfaceTests.
template<typename T>
class IListener
{
public:
    virtual ~IListener() = default;
    virtual void OnEvent(const T& evt) = 0;
    virtual int Priority() const noexcept = 0;
};

} // namespace mini::bus
