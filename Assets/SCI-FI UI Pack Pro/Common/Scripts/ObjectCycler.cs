using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(UnityEngine.Canvas))] // 如果你主要用于UI，可以加上这个，确保有Canvas组件
public class ObjectCycler : MonoBehaviour
{
    // 1. 公开的列表，你可以在Unity编辑器中拖拽GameObject进来
    [Tooltip("在这里放入你想要轮流显示的所有游戏对象。")]
    public List<GameObject> objectsToCycle = new List<GameObject>();

    // 2. 私有变量，用于跟踪当前显示的对象索引
    private int currentIndex = 0;

    void Start()
    {
        // 3. 在游戏开始时，初始化对象的显示状态
        InitializeCycle();
    }

    void Update()
    {
        // 4. 检测键盘输入
        HandleInput();
    }

    /// <summary>
    /// 初始化函数，确保只有第一个对象是可见的
    /// </summary>
    private void InitializeCycle()
    {
        // 安全检查：如果列表为空或只有一个元素，则无需操作
        if (objectsToCycle.Count <= 1)
        {
            // 如果列表不为空，确保第一个对象是激活的
            if (objectsToCycle.Count == 1 && objectsToCycle[0] != null)
            {
                objectsToCycle[0].SetActive(true);
            }
            return;
        }

        // 遍历列表，隐藏所有对象
        foreach (var obj in objectsToCycle)
        {
            if (obj != null) // 额外的安全检查，防止列表中有空引用
            {
                obj.SetActive(false);
            }
        }

        // 激活列表中的第一个对象
        if (objectsToCycle[0] != null)
        {
            objectsToCycle[0].SetActive(true);
        }
        currentIndex = 0; // 确保索引从0开始
    }

    /// <summary>
    /// 处理用户的键盘输入
    /// </summary>
    private void HandleInput()
    {
        // 如果列表中没有对象或只有一个对象，则不响应输入
        if (objectsToCycle.Count <= 1)
        {
            return;
        }

        // 检测右方向键输入，显示下一个对象
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            CycleToNextObject();
        }

        // 检测左方向键输入，显示上一个对象
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            CycleToPreviousObject();
        }
    }

    /// <summary>
    /// 切换到下一个对象
    /// </summary>
    public void CycleToNextObject()
    {
        // 隐藏当前显示的对象
        objectsToCycle[currentIndex].SetActive(false);

        // 更新索引。使用取模运算(%)实现循环
        // 例如：如果当前是最后一个(index = 4, count = 5)，(4 + 1) % 5 = 0，回到第一个
        currentIndex = (currentIndex + 1) % objectsToCycle.Count;

        // 显示新索引对应的对象
        objectsToCycle[currentIndex].SetActive(true);

        Debug.Log("切换到下一个对象: " + objectsToCycle[currentIndex].name);
    }

    /// <summary>
    /// 切换到上一个对象
    /// </summary>
    public void CycleToPreviousObject()
    {
        // 隐藏当前显示的对象
        objectsToCycle[currentIndex].SetActive(false);

        // 更新索引。这个技巧可以实现向下循环
        // 例如：如果当前是第一个(index = 0, count = 5)，(0 - 1 + 5) % 5 = 4，跳到最后一个
        currentIndex = (currentIndex - 1 + objectsToCycle.Count) % objectsToCycle.Count;

        // 显示新索引对应的对象
        objectsToCycle[currentIndex].SetActive(true);

        Debug.Log("切换到上一个对象: " + objectsToCycle[currentIndex].name);
    }
}