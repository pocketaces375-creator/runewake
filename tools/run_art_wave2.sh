#!/bin/bash
set -a
source ~/.hermes/.env
set +a
cd /home/fictive/runewake-lane2
exec python3 -u pipeline/work/gen_art_wave2.py