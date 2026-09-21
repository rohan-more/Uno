# The pluginbuilder, the Nakama image and nakama-common in go.mod must all be
# the same release, or Nakama refuses to load the plugin.
FROM heroiclabs/nakama-pluginbuilder:3.40.0 AS builder

ENV GO111MODULE=on
ENV CGO_ENABLED=1

WORKDIR /backend
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN go build --trimpath --buildmode=plugin -o ./backend.so

FROM heroiclabs/nakama:3.40.0

COPY --from=builder /backend/backend.so /nakama/data/modules/
COPY local.yml /nakama/data/
