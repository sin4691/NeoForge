using System.Collections.Generic;
using Factory.Building;
using NUnit.Framework;
using UnityEngine;

public class BeltPathBuilderTests
{
    [Test]
    public void Extend_AdjacentCells_AppendsDirectly()
    {
        var path = new List<Vector2Int>();
        BeltPathBuilder.Extend(path, new Vector2Int(0, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 0));

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) },
            path);
    }

    [Test]
    public void Extend_DiagonalJump_InsertsLShapedCorner()
    {
        var path = new List<Vector2Int>();
        BeltPathBuilder.Extend(path, new Vector2Int(0, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 1)); // 손가락이 빠르게 움직여 대각선으로 튐

        // 우세 축(x, delta=2)을 먼저 채우고 나머지(y, delta=1)를 이어붙여야 함
        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(2, 1) },
            path);
    }

    [Test]
    public void Extend_BacktrackToEarlierCell_TrimsPath()
    {
        var path = new List<Vector2Int>();
        BeltPathBuilder.Extend(path, new Vector2Int(0, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 0)); // 왔던 길을 되짚음 -> 취소 제스처

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) },
            path);
    }

    [Test]
    public void Extend_FastSkipCrossesEarlierPath_TrimsAtCrossingPoint()
    {
        var path = new List<Vector2Int>();
        BeltPathBuilder.Extend(path, new Vector2Int(0, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 1));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 2));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 2));
        BeltPathBuilder.Extend(path, new Vector2Int(0, 2)); // 여기까지 'ㄷ'자 경로

        // 드래그가 너무 빨라서 (0,2)에서 (1,1)로 건너뛰는데, 중간을 채우는 과정(우세축 먼저:
        // x)에서 (1,2)를 지나가게 된다 — 그런데 (1,2)는 이미 지나온 칸이다. 그 지점까지만
        // 잘라내고 멈춰야 한다 — 안 그러면 (1,2)가 경로에 두 번 들어가는 꼬인 경로가 된다
        // (실제로 겪은 버그: 빠르게 드래그하면 가끔 벨트가 꼬여서 중복 설치됨).
        BeltPathBuilder.Extend(path, new Vector2Int(1, 1));

        CollectionAssert.AreEqual(
            new[]
            {
                new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0),
                new Vector2Int(2, 1), new Vector2Int(2, 2), new Vector2Int(1, 2),
            },
            path);
    }

    [Test]
    public void BuildOrthogonalPath_FromRawCellList_MatchesIncrementalExtend()
    {
        var raw = new List<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 2) };
        var path = BeltPathBuilder.BuildOrthogonalPath(raw);

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(1, 2) },
            path);
    }
}
