function showTime() {
    const now = new Date();
    document.getElementById('result').innerText =
        'เวลาปัจจุบัน: ' + now.toLocaleString();
}
