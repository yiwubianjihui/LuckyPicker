#!/bin/sh
# 幸运摇人器 启动脚本（解压即用）
DIR=$(dirname "$(readlink -f "$0")")
exec "$DIR/lucky-picker" --no-sandbox "$@"
