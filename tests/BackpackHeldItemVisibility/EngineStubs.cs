// Minimal engine boundary for lifecycle tests of the real visibility component.
// Rendering and Unity's callback scheduling still require an in-game check.
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public bool IsDestroyed;
        public bool DestroyPending;
        public static implicit operator bool(Object? value) => value != null && !value.IsDestroyed;
        public static void Destroy(Object value) => value.DestroyPending = true;
    }

    public class Component : Object
    {
        public GameObject gameObject = null!;
    }

    public class MonoBehaviour : Component { }

    public class Renderer : Component
    {
        public bool enabled = true;
        public bool forceRenderingOff;
    }

    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        private readonly List<Component> _components = new();
        public readonly List<GameObject> Children = new();
        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
            component.gameObject = this;
            _components.Add(component);
            return component;
        }
        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().Where(c => c).ToArray();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            GetComponents<T>().Concat(Children.SelectMany(c => c.GetComponentsInChildren<T>(includeInactive))).ToArray();
    }

    public static class Time
    {
        public static float unscaledTime;
    }
}

namespace EFT
{
    public class Player : UnityEngine.Object
    {
        public Controller? HandsController;
        public Health? HealthController = new();
    }
    public class Controller
    {
        public UnityEngine.GameObject ControllerGameObject = new();
    }
    public class Health
    {
        public bool IsAlive = true;
    }
}
