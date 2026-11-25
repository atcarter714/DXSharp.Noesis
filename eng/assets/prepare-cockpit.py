#!/usr/bin/env python3
"""Bake the supplied cockpit glTF into DXSharp's current POSITION/NORMAL/COLOR format.
Requires numpy and trimesh. The original asset remains unchanged. This is a preview
conversion, not a full PBR asset importer. A calibrated eye position is required.
"""
import argparse
import pathlib
import struct
import numpy as np
import trimesh

ROOT = pathlib.Path(__file__).resolve().parents[2]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('gltf', type=pathlib.Path)
    p.add_argument('--eye', nargs=3, type=float, required=True, metavar=('X','Y','Z'),
                   help='pilot eye in source glTF world coordinates')
    p.add_argument('--forward', choices=['+z','-z'], default='-z')
    p.add_argument('--scale', type=float, default=1.0)
    p.add_argument('--output', type=pathlib.Path, default=ROOT/'samples/Starfighter/Assets/cockpit.mesh')
    a = p.parse_args()
    if not np.isfinite(a.eye).all() or not np.isfinite(a.scale) or a.scale <= 0:
        p.error('eye and scale must be finite; scale must be positive')
    scene = trimesh.load(a.gltf, force='scene', process=False)
    # Flip Z for the usual glTF -Z forward authoring convention; adjust winding.
    basis = np.diag([1., 1., -1. if a.forward == '-z' else 1.])
    chunks = []
    vertices, indices, count = [], [], 0
    def flush():
        nonlocal vertices, indices, count
        if count:
            chunks.append((np.concatenate(vertices).astype('<f4'), np.concatenate(indices).astype('<u2')))
        vertices, indices, count = [], [], 0
    for node in sorted(scene.graph.nodes_geometry):
        transform, geometry = scene.graph[node]
        mesh = scene.geometry[geometry]
        if not len(mesh.faces): continue
        pos = ((trimesh.transform_points(mesh.vertices, transform) - np.asarray(a.eye)) @ basis.T) * a.scale
        linear = basis @ transform[:3,:3]
        normal = mesh.vertex_normals @ np.linalg.inv(linear)
        length = np.linalg.norm(normal, axis=1)
        normal[length < 1e-12] = (0,1,0)
        normal /= np.linalg.norm(normal, axis=1)[:,None]
        material = getattr(mesh.visual, 'material', None)
        color = getattr(material, 'baseColorFactor', None)
        if color is None: color = [140,140,140,255]
        raw_color = np.asarray(color)[:3]
        color = raw_color.astype(np.float64)
        if np.issubdtype(raw_color.dtype, np.integer) or color.max() > 1: color /= 255.
        color = np.clip(color, 0., 1.)
        faces = np.asarray(mesh.faces).copy()
        if np.linalg.det(linear) < 0: faces = faces[:,[0,2,1]]
        # Split by triangle ranges, compacting each chunk's local vertex references.
        for start in range(0, len(faces), 20000):
            face = faces[start:start+20000]
            used, remapped = np.unique(face, return_inverse=True)
            if count + len(used) > 65535: flush()
            data = np.column_stack((pos[used], normal[used], np.broadcast_to(color,(len(used),3))))
            if not np.isfinite(data).all(): raise ValueError(f'Nonfinite mesh: {node}')
            vertices.append(data); indices.append(remapped.reshape(-1)+count); count += len(used)
    flush()
    if not chunks: raise ValueError('No triangle geometry found')
    a.output.parent.mkdir(parents=True, exist_ok=True)
    tmp = a.output.with_suffix('.tmp')
    with tmp.open('wb') as f:
        f.write(b'DXCM'); f.write(struct.pack('<II',1,len(chunks)))
        for verts, inds in chunks:
            f.write(struct.pack('<II',len(verts),len(inds))); f.write(verts.tobytes()); f.write(inds.tobytes())
    tmp.replace(a.output)
    print(f'{a.output}: {len(chunks)} chunks, {sum(len(v) for v,i in chunks)} vertices, {sum(len(i)//3 for v,i in chunks)} triangles')

if __name__ == '__main__': main()
