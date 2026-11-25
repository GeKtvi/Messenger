#!/bin/sh
# Fakes the distance between the regions. Each region has its own Toxiproxy container ("wan-us"): it
# forwards each port to a service in another region and delays the traffic both ways by half the round
# trip. Only traffic sent to a wan container is delayed: the addresses in docker-compose.yml decide that.
set -eu

# post <url> <json>
post() {
    status=$(curl -s -o /dev/null -w '%{http_code}' --retry 10 --retry-connrefused --retry-delay 1 -X POST "$1" -d "$2")
    case $status in
        2* | 409) ;;
        *) echo "POST $1 answered $status" >&2; exit 1 ;;
    esac
}

# link <from region> <listen port> <target> <round trip in ms>
link() {
    api="http://wan-$1:8474"
    name="port-$2"
    # 409 means it's already there, from an earlier run. Recreating it would cut open connections.
    post "$api/proxies" "{\"name\":\"$name\",\"listen\":\"0.0.0.0:$2\",\"upstream\":\"$3\",\"enabled\":true}"
    for stream in upstream downstream; do
        post "$api/proxies/$name/toxics" \
            "{\"name\":\"$stream\",\"type\":\"latency\",\"stream\":\"$stream\",\"attributes\":{\"latency\":$(($4 / 2))}}"
    done
}

# apis <from region> <to region> <first port> <round trip in ms>
apis() {
    link "$1" "$3" "identity-$2:8080" "$4"
    link "$1" "$(($3 + 1))" "messages-$2:8080" "$4"
}

# shared <from region> <round trip in ms>: what exists only in the EU
shared() {
    link "$1" 5432 postgres:5432 "$2"
    # The replica that is promoted if postgres fails.
    link "$1" 5433 postgres-eu:5432 "$2"
    link "$1" 6379 redis:6379 "$2"
    link "$1" 5672 rabbitmq:5672 "$2"
}

# In every region: 8101/8102 is the EU's identity/messages, 8201/8202 the US's, 8301/8302 Asia's.
EU_US=90
EU_ASIA=200
US_ASIA=150

apis eu us 8201 $EU_US
apis eu asia 8301 $EU_ASIA

apis us eu 8101 $EU_US
apis us asia 8301 $US_ASIA
shared us $EU_US

apis asia eu 8101 $EU_ASIA
apis asia us 8201 $US_ASIA
shared asia $EU_ASIA
