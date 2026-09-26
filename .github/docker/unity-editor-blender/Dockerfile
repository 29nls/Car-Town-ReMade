# Unity 2020.1.8f1 editor with Blender installed on top, so the .blend models
# under Assets/ are converted to FBX while the project is imported instead of
# being skipped with "Blender could not be found".
#
# The .blend files in this repository were saved by Blender 3.0 (their headers
# read "BLENDER-v301"/"v303"), and Blender refuses to open files written by a
# newer version than itself, so a 3.x release is required.
#
# Published by .github/workflows/unity-ci.yml as
# ghcr.io/<owner>/car-town-unity-ci:2020.1.8f1-blender-<dockerfile hash>.

ARG GAMECI_IMAGE=unityci/editor:2020.1.8f1-linux-il2cpp-3
FROM ${GAMECI_IMAGE}

ARG BLENDER_SHORT_VERSION=3.3
ARG BLENDER_FULL_VERSION=3.3.15

# Unity drives Blender headless for the .blend -> FBX conversion, so the plain
# download is enough. The tarball is deleted in the same layer to keep the image
# from growing by another ~200 MB.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates wget xz-utils \
    && wget -q "https://download.blender.org/release/Blender${BLENDER_SHORT_VERSION}/blender-${BLENDER_FULL_VERSION}-linux-x64.tar.xz" \
    && tar -xf "blender-${BLENDER_FULL_VERSION}-linux-x64.tar.xz" -C /opt \
    && rm "blender-${BLENDER_FULL_VERSION}-linux-x64.tar.xz" \
    && ln -s "/opt/blender-${BLENDER_FULL_VERSION}-linux-x64/blender" /usr/local/bin/blender \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Unity's Blender importer looks the executable up on PATH.
ENV PATH="/opt/blender-${BLENDER_FULL_VERSION}-linux-x64:${PATH}"

# The WebGL build compresses its output; some editor builds look for the brotli
# tool on PATH instead of shipping one, so install it when the base image
# offers it. Missing it must not break the image.
RUN apt-get update \
    && (apt-get install -y --no-install-recommends brotli || echo "brotli package unavailable, continuing") \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Turns a broken download into a failed image build instead of failing imports.
RUN blender --version
