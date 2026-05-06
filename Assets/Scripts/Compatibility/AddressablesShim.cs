using UnityEngine;

namespace UnityEngine.AddressableAssets
{
    using UnityEngine.ResourceManagement.AsyncOperations;

    public static class Addressables
    {
        public static AsyncOperationHandle<T> LoadAssetAsync<T>(string key) where T : Object
        {
            return new AsyncOperationHandle<T>(null);
        }

        public static AsyncOperationHandle<GameObject> InstantiateAsync(string key, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(key ?? "Addressable_Instance");
            go.transform.position = position;
            go.transform.rotation = rotation;
            return new AsyncOperationHandle<GameObject>(go);
        }

        public static void Release<T>(AsyncOperationHandle<T> handle) { }
        public static void ReleaseInstance(GameObject instance)
        {
            if (instance != null) Object.Destroy(instance);
        }
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None = 0, Succeeded = 1, Failed = 2 }

    public struct AsyncOperationHandle<T>
    {
        public AsyncOperationStatus Status;
        public T Result;

        public AsyncOperationHandle(T result)
        {
            Status = result == null ? AsyncOperationStatus.Failed : AsyncOperationStatus.Succeeded;
            Result = result;
        }

        public bool IsValid() => Result != null;
    }
}
