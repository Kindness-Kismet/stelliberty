use std::time::{Duration, Instant};

pub(crate) const TIMEOUT: Duration = Duration::from_secs(70);
pub(crate) const CHECK_INTERVAL: Duration = Duration::from_secs(45);
// 检查周期 45 秒之外再留 15 秒调度余量，停顿后给托盘完整租期续接。
const PAUSE_THRESHOLD: Duration = Duration::from_secs(60);

pub(crate) struct HeartbeatWatchdog {
    last_check: Instant,
    grace_started: Option<Instant>,
}

impl HeartbeatWatchdog {
    pub(crate) fn new(now: Instant) -> Self {
        Self {
            last_check: now,
            grace_started: None,
        }
    }

    pub(crate) fn observe(&mut self, now: Instant) -> Option<Duration> {
        let gap = now.saturating_duration_since(self.last_check);
        self.last_check = now;
        if gap > PAUSE_THRESHOLD {
            self.grace_started = Some(now);
            Some(gap)
        } else {
            None
        }
    }

    pub(crate) fn should_expire(&self, now: Instant, heartbeat: Option<Instant>) -> bool {
        heartbeat.is_some_and(|heartbeat| now.saturating_duration_since(heartbeat) > TIMEOUT)
            && self
                .grace_started
                .is_none_or(|started| now.saturating_duration_since(started) > TIMEOUT)
    }
}
