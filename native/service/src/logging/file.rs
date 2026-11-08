use std::collections::VecDeque;
use std::fs::{self, File, OpenOptions};
use std::io::{self, BufRead, BufReader, Write};
use std::path::{Path, PathBuf};

const LOG_FILE_NAME: &str = "service.logs";
const PREVIOUS_LOG_FILE_NAME: &str = "service.previous.logs";
// 当前日志和上一份各最多 4 MiB，保留跨重启诊断且限制磁盘占用。
const MAX_FILE_BYTES: u64 = 4 * 1024 * 1024;

pub(super) struct FileLog {
    path: PathBuf,
    previous_path: PathBuf,
}

impl FileLog {
    pub fn open() -> io::Result<Self> {
        let (path, previous_path) = paths()?;
        open_append(&path)?;
        Ok(Self {
            path,
            previous_path,
        })
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn append(&self, line: &str) -> io::Result<()> {
        let record = format!("{line}\n");
        let mut file = open_append(&self.path)?;
        if file.metadata()?.len() + record.len() as u64 > MAX_FILE_BYTES {
            // Windows 重命名前释放文件句柄，旧备份由新一轮日志替换。
            drop(file);
            fs::rename(&self.path, &self.previous_path)?;
            file = open_append(&self.path)?;
        }
        file.write_all(record.as_bytes())?;
        // 关机期间不依赖后台缓冲任务，关键记录在返回前提交给文件系统。
        file.sync_data()
    }
}

pub(super) fn read_recent(count: usize) -> io::Result<Vec<String>> {
    let (path, previous_path) = paths()?;
    let mut lines = VecDeque::with_capacity(count);
    for source in [previous_path, path] {
        let file = match File::open(source) {
            Ok(file) => file,
            Err(error) if error.kind() == io::ErrorKind::NotFound => continue,
            Err(error) => return Err(error),
        };
        for line in BufReader::new(file).lines() {
            let line = line?;
            if count == 0 {
                continue;
            }
            if lines.len() == count {
                lines.pop_front();
            }
            lines.push_back(line);
        }
    }
    Ok(lines.into_iter().collect())
}

fn paths() -> io::Result<(PathBuf, PathBuf)> {
    // 服务以系统账户运行时，工作目录和用户目录都不代表安装位置。
    let executable = std::env::current_exe()?;
    let directory = executable.parent().ok_or_else(|| {
        io::Error::new(
            io::ErrorKind::InvalidInput,
            "Service executable has no parent directory",
        )
    })?;
    Ok((
        directory.join(LOG_FILE_NAME),
        directory.join(PREVIOUS_LOG_FILE_NAME),
    ))
}

fn open_append(path: &Path) -> io::Result<File> {
    OpenOptions::new().create(true).append(true).open(path)
}
