﻿using System.Collections.Generic;
using SaberFactory.Misc;
using UnityEngine;

namespace SaberFactory.Instances.Trail
{
    internal class AltTrail : MonoBehaviour
    {
        public class Element
        {
            public Vector3 PointEnd;
            public Vector3 PointStart;

            public Element(Vector3 start, Vector3 end)
            {
                PointStart = start;
                PointEnd = end;
            }

            public Element()
            {
            }

            public Vector3 Pos => (PointStart + PointEnd) / 2f;
        }

        public class ElementPool
        {
            private readonly Stack<Element> _stack = new Stack<Element>();

            public ElementPool(int preCount)
            {
                for (var i = 0; i < preCount; i++)
                {
                    var element = new Element();
                    _stack.Push(element);
                    CountAll++;
                }
            }

            public int CountAll { get; private set; }

            public Element Get()
            {
                Element element;
                if (_stack.Count == 0)
                {
                    element = new Element();
                    CountAll++;
                }
                else
                {
                    element = _stack.Pop();
                }

                return element;
            }

            public void Release(Element element)
            {
                if (_stack.Count > 0 && ReferenceEquals(_stack.Peek(), element))
                    Debug.LogError("Internal error. Trying to destroy object that is already released to pool.");

                _stack.Push(element);
            }
        }

        public string SortingLayerName;
        public int SortingOrder;
        public Transform PointStart;
        public Transform PointEnd;

        public int TrailLength = 30;
        public int Granularity = 60;
        public float Whitestep;
        public Color MyColor = Color.white;
        public Material MyMaterial;

        protected float _trailWidth;
        protected List<Element> _snapshotList = new List<Element>();
        protected ElementPool _elemPool;
        protected Spline _spline = new Spline();
        protected VertexPool _vertexPool;
        protected VertexPool.VertexSegment _vertexSegment;
        protected bool _inited;

        private int _skipFirstFrames = 4;
        private int _frameNum;

        public Vector3 CurHeadPos => (PointStart.position + PointEnd.position) / 2f;

        public void Setup(TrailInitData initData, Transform pointStart, Transform pointEnd, Material material)
        {
            PointStart = pointStart;
            PointEnd = pointEnd;
            MyMaterial = material;
            Granularity = initData.Granularity;
            TrailLength = initData.TrailLength;
            Whitestep = initData.Whitestep;

            _elemPool = new ElementPool(TrailLength);
            _trailWidth = (PointStart.position - PointEnd.position).magnitude;
            _vertexPool = new VertexPool(MyMaterial, this);
            _vertexSegment = _vertexPool.GetVertices(Granularity * 3, (Granularity - 1) * 12);
            UpdateIndices();

            _vertexPool.SetMeshObjectActive(false);

            _inited = true;
        }

        private void OnDisable()
        {
            _vertexPool.SetMeshObjectActive(false);
        }

        private void OnEnable()
        {
            _vertexPool?.SetMeshObjectActive(true);
        }

        private void LateUpdate()
        {
            if (!_inited)
                return;

            _frameNum++;

            if (_frameNum == _skipFirstFrames + 1)
            {
                _vertexPool.SetMeshObjectActive(true);

                _spline.Granularity = Granularity;
                _spline.Clear();
                for (int i = 0; i < TrailLength; i++)
                {
                    _spline.AddControlPoint(CurHeadPos, PointStart.position - PointEnd.position);
                }
                _snapshotList.Clear();
                _snapshotList.Add(new Element(PointStart.position, PointEnd.position));
                _snapshotList.Add(new Element(PointStart.position, PointEnd.position));
            }
            else if (_frameNum < _skipFirstFrames + 1)
            {
                return;
            }

            UpdateHeadElem();
            RecordCurElem();
            RefreshSpline();
            UpdateVertex();
            _vertexPool.LateUpdate();
        }

        private void OnDestroy()
        {
            if (!_inited || _vertexPool == null) return;

            _vertexPool.Destroy();
        }

        private void RefreshSpline()
        {
            for (var i = 0; i < _snapshotList.Count; i++)
            {
                _spline.ControlPoints[i].Position = _snapshotList[i].Pos;
                _spline.ControlPoints[i].Normal = _snapshotList[i].PointEnd - _snapshotList[i].PointStart;
            }

            _spline.RefreshSpline();
        }

        private void UpdateVertex()
        {
            var pool = _vertexSegment.Pool;


            for (var i = 0; i < Granularity; i++)
            {
                var baseIdx = _vertexSegment.VertStart + i * 3;

                var uvSegment = (float) i / Granularity;

                var uvCoord = Vector2.zero;

                var pos = _spline.InterpolateByLen(uvSegment);

                var up = _spline.InterpolateNormalByLen(uvSegment);
                var pos0 = pos + up.normalized * _trailWidth * 0.5f;
                var pos1 = pos - up.normalized * _trailWidth * 0.5f;

                var color = baseIdx < Whitestep ? Color.white : MyColor;

                // pos0
                pool.Vertices[baseIdx] = pos0;
                pool.Colors[baseIdx] = color;
                uvCoord.x = 0f;
                uvCoord.y = uvSegment;
                pool.UVs[baseIdx] = uvCoord;

                //pos
                pool.Vertices[baseIdx + 1] = pos;
                pool.Colors[baseIdx + 1] = color;
                uvCoord.x = 0.5f;
                uvCoord.y = uvSegment;
                pool.UVs[baseIdx + 1] = uvCoord;

                //pos1
                pool.Vertices[baseIdx + 2] = pos1;
                pool.Colors[baseIdx + 2] = color;
                uvCoord.x = 1f;
                uvCoord.y = uvSegment;
                pool.UVs[baseIdx + 2] = uvCoord;
            }

            _vertexSegment.Pool.UVChanged = true;
            _vertexSegment.Pool.VertChanged = true;
            _vertexSegment.Pool.ColorChanged = true;
        }

        private void UpdateIndices()
        {
            var pool = _vertexSegment.Pool;

            for (var i = 0; i < Granularity - 1; i++)
            {
                var baseIdx = _vertexSegment.VertStart + i * 3;
                var nextBaseIdx = _vertexSegment.VertStart + (i + 1) * 3;

                var iidx = _vertexSegment.IndexStart + i * 12;

                //triangle left
                pool.Indices[iidx + 0] = nextBaseIdx;
                pool.Indices[iidx + 1] = nextBaseIdx + 1;
                pool.Indices[iidx + 2] = baseIdx;
                pool.Indices[iidx + 3] = nextBaseIdx + 1;
                pool.Indices[iidx + 4] = baseIdx + 1;
                pool.Indices[iidx + 5] = baseIdx;

                //triangle right
                pool.Indices[iidx + 6] = nextBaseIdx + 1;
                pool.Indices[iidx + 7] = nextBaseIdx + 2;
                pool.Indices[iidx + 8] = baseIdx + 1;
                pool.Indices[iidx + 9] = nextBaseIdx + 2;
                pool.Indices[iidx + 10] = baseIdx + 2;
                pool.Indices[iidx + 11] = baseIdx + 1;
            }

            pool.IndiceChanged = true;
        }

        private void UpdateHeadElem()
        {
            _snapshotList[0].PointStart = PointStart.position;
            _snapshotList[0].PointEnd = PointEnd.position;
        }

        private void RecordCurElem()
        {
            var elem = _elemPool.Get();
            elem.PointStart = PointStart.position;
            elem.PointEnd = PointEnd.position;

            if (_snapshotList.Count < TrailLength)
            {
                _snapshotList.Insert(1, elem);
            }
            else
            {
                _elemPool.Release(_snapshotList[_snapshotList.Count - 1]);
                _snapshotList.RemoveAt(_snapshotList.Count - 1);
                _snapshotList.Insert(1, elem);
            }
        }
    }
}