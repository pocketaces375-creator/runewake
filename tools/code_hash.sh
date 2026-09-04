#!/usr/bin/env bash
# The identity of the code the gate actually tests. Queue/state/log/doc commits do not change it,
# so all five lanes share one verdict and only a real code change earns a new one.
cd "${1:-.}" 2>/dev/null || exit 1
git rev-parse HEAD:client HEAD:engine HEAD:content HEAD:tests HEAD:sim 2>/dev/null | md5sum | cut -c1-12
