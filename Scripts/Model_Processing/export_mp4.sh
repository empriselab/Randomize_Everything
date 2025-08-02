#!/bin/bash

cd RCareUnity/Assets/VideoFrames

for dir in Room.*; do
  echo "Converting $dir..."
  ffmpeg -y -framerate 15 -i "$dir/frame_%04d.png" -c:v libx264 -pix_fmt yuv420p "${dir}.mp4"
done
