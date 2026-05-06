using System.Collections.Generic;
using UnityEngine;

namespace Ecopoly.Utils
{
    /// <summary>
    /// Generic object pool.
    /// Usage: var pool = new ObjectPool<ParticleSystem>(prefab, transform, 10);
    ///        var instance = pool.Get();
    ///        pool.Return(instance);
    /// </summary>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Queue<T> _pool = new Queue<T>();

        public ObjectPool(T prefab, Transform parent, int initialSize = 5)
        {
            _prefab = prefab;
            _parent = parent;
            for (int i = 0; i < initialSize; i++)
                _pool.Enqueue(CreateInstance());
        }

        public T Get()
        {
            T instance = _pool.Count > 0 ? _pool.Dequeue() : CreateInstance();
            instance.gameObject.SetActive(true);
            return instance;
        }

        public void Return(T instance)
        {
            instance.gameObject.SetActive(false);
            instance.transform.SetParent(_parent);
            _pool.Enqueue(instance);
        }

        private T CreateInstance()
        {
            T obj = Object.Instantiate(_prefab, _parent);
            obj.gameObject.SetActive(false);
            return obj;
        }

        public void Clear()
        {
            while (_pool.Count > 0)
            {
                var obj = _pool.Dequeue();
                if (obj != null) Object.Destroy(obj.gameObject);
            }
        }
    }

    /// <summary>
    /// Simple GameObject pool.
    /// </summary>
    public class GameObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        public GameObjectPool(GameObject prefab, Transform parent, int initialSize = 5)
        {
            _prefab = prefab;
            _parent = parent;
            for (int i = 0; i < initialSize; i++)
                _pool.Enqueue(CreateInstance());
        }

        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            GameObject instance = _pool.Count > 0 ? _pool.Dequeue() : CreateInstance();
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            return instance;
        }

        public void Return(GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(_parent);
            _pool.Enqueue(instance);
        }

        private GameObject CreateInstance()
        {
            var obj = Object.Instantiate(_prefab, _parent);
            obj.SetActive(false);
            return obj;
        }
    }
}
