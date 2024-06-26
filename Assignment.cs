﻿#include <iostream>
#include <cstdlib> // for rand()
#include <ctime>   // for time()
#include <list>
#include <mutex>
#include <thread>
#include <chrono>
#include <sstream>

using namespace std;

// 프로세스 구조체
struct Process
{
    int pid;            // 프로세스 ID
    bool isForeground;  // foreground인지 여부
    int remainingTime;  // 백그라운드 프로세스의 남은 시간
    bool isPromoted;    // 프로모션된 프로세스 여부

    // 생성자
    Process(int id, bool fg = true) : pid(id), isForeground(fg), remainingTime(0), isPromoted(false) { }
};

// 스택 노드 구조체
struct StackNode
{
    list<Process> processList; // 프로세스 리스트
    StackNode* next;           // 다음 노드를 가리키는 포인터

    // 생성자
    StackNode() : next(nullptr) { }
};

// 대기 큐 구조체
struct WaitQueue
{
    list<Process> processList; // 프로세스 리스트
    mutex mtx;                 // 뮤텍스
};

class DynamicQueue
{
    private:
    StackNode* top;    // 스택의 맨 위 (foreground 프로세스)
    StackNode* bottom; // 스택의 맨 아래 (background 프로세스)
    mutex mtx;         // 뮤텍스
    WaitQueue wq;      // 대기 큐
    bool topFirst;     // top과 bottom의 출력 순서를 결정하기 위한 플래그

    public:
    // 생성자
    DynamicQueue() : top(nullptr), bottom(nullptr), topFirst(true) { }

    // 프로세스 enqueue
    void enqueue(Process p)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode * &node = p.isForeground ? top : bottom;
        node = ensureNode(node);
        node->processList.push_back(p);
    }

    // 프로세스 dequeue
    Process dequeue()
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* node = top;
        if (!node || node->processList.empty()) return Process(-1, true);
        Process p = node->processList.back();
        node->processList.pop_back();
        if (node->processList.empty())
        {
            top = node->next;
            delete node;
        }
        return p;
    }

    // 프로세스 프로모션
    void promote()
    {
        lock_guard < mutex > lock (mtx) ;
        if (!bottom || bottom->processList.empty()) return;
        Process promotedProcess = bottom->processList.front();
        bottom->processList.pop_front();
        promotedProcess.isPromoted = true;
        top = ensureNode(top);
        top->processList.push_back(promotedProcess);
    }

    // 프로세스 분할 및 병합
    void split_n_merge(int threshold)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = top;
        while (targetNode)
        {
            if (targetNode->processList.size() > threshold)
            {
                StackNode* newNode = new StackNode();
                auto it = targetNode->processList.begin();
                advance(it, targetNode->processList.size() / 2);
                newNode->processList.splice(newNode->processList.begin(), targetNode->processList, it, targetNode->processList.end());
                newNode->next = targetNode->next;
                targetNode->next = newNode;
            }
            targetNode = targetNode->next;
        }
    }

    // 노드의 유무 확인 및 생성
    StackNode* ensureNode(StackNode* node)
    {
        if (!node)
        {
            node = new StackNode();
            if (!top) top = node;
            if (!bottom) bottom = node;
        }
        return node;
    }

    // 큐의 내용 출력
    void printQueue()
    {
        lock_guard < mutex > lock (mtx) ;
        stringstream ss;
        ss << "Running: " << (top && !top->processList.empty() ? "[" + to_string(top->processList.back().pid) + (top->processList.back().isForeground ? "F" : "B") + "]" : "[]") << "\n--------------------------\n";
        ss << "DQ:\n" << printNode(top, "(top)") << printNode(bottom, "(bottom)") << "--------------------------\n";
        ss << "WQ: ";
        lock_guard<mutex> wqLock(wq.mtx);
        for (const auto&process : wq.processList) {
            ss << "[" << process.pid << (process.isForeground ? "F" : "B") << ":" << process.remainingTime << "] ";
        }
        ss << "\n--------------------------\n";
        cout << ss.str();
    }

    // 노드의 내용을 문자열로 반환
    string printNode(StackNode* node, const string& label) {
        stringstream ss;
        if (node) {
            if (!label.empty()) ss << label << "\n";
            bool printedPromoted = false;
            for (const auto& process : node->processList) {
                if (process.isPromoted && !printedPromoted) {
                    ss << "P => ";
                    printedPromoted = true;
                }
ss << "[" << process.pid << (process.isForeground? "F" : "B") << (process.isPromoted? "*" : "") << "] ";
            }
            ss << "\n";
        }
        return ss.str();
    }

    // 대기 큐에 프로세스 추가
    void addToWaitQueue(Process p)
{
    lock_guard < mutex > lock (wq.mtx) ;
    wq.processList.push_back(p);
}

// 대기 큐 처리
void processWaitQueue()
{
    lock_guard < mutex > lock (wq.mtx) ;
    for (auto it = wq.processList.begin(); it != wq.processList.end();)
    {
        it->remainingTime--;
        if (it->remainingTime <= 0)
        {
            enqueue(*it);
            it = wq.processList.erase(it);
        }
        else
        {
            ++it;
        }
    }
}

// 백그라운드 프로세스 대기 큐로 이동
void moveToWaitQueue()
{
    lock_guard < mutex > lock (mtx) ;
    if (!bottom || bottom->processList.empty()) return;
    auto it = bottom->processList.begin();
    while (it != bottom->processList.end())
    {
        if (!it->isForeground && it->remainingTime > 0)
        {
            addToWaitQueue(*it);
            it = bottom->processList.erase(it);
        }
        else
        {
            ++it;
        }
    }
}
};

// 프로세스 시뮬레이션 함수
void simulateProcesses(DynamicQueue& queue)
{
    srand(time(nullptr));
    int count = 0;
    while (count < 20)
    { // 20번의 반복으로 시뮬레이션
        // 프로세스 생성 시뮬레이션
        int pid = rand() % 100; // 랜덤 프로세스 ID 생성
        bool isForeground = rand() % 2 == 0; // 프로세스가 foreground 또는 background인지 랜덤으로 결정
        Process p(pid, isForeground);
        if (!isForeground) p.remainingTime = rand() % 10 + 1; // 백그라운드 프로세스의 잔여 시간 랜덤 생성

        // 프로세스 enqueue
        queue.enqueue(p);

        // 대기 큐 처리
        queue.processWaitQueue();

        // 프로세스 프로모션 및 분할/병합
        queue.promote();
        queue.split_n_merge(5); // 데모를 위해 임계값을 5로 설정

        // 백그라운드 프로세스를 대기 큐로 이동
        queue.moveToWaitQueue();

        // 큐 출력
        queue.printQueue();

        // 실제 시간 처리를 시뮬레이션하기 위해 짧은 시간 동안 sleep
        this_thread::sleep_for(chrono::seconds(1));

        count++;
    }
}

int main()
{
    DynamicQueue queue;
    simulateProcesses(queue);
    return 0;
}
//2-1
#include <iostream>
#include <cstdlib> // for exit()
#include <cstring> // for strtok(), strtok_r()
#include <unistd.h> // for fork(), execvp(), wait()
#include <thread>
#include <chrono>
#include <list>
#include <mutex>
#include <algorithm>
#include <sys/wait.h> // for wait function

using namespace std;

// 프로세스 구조체
struct Process
{
    int pid; // 프로세스 ID
    bool isForeground; // 포그라운드인 경우 true, 백그라운드인 경우 false
    int remainingTime; // 백그라운드 프로세스의 남은 시간
    Process(int id, bool fg = true) : pid(id), isForeground(fg), remainingTime(0) { }
};

// 스택 노드 구조체
struct StackNode
{
    list<Process> processList; // 프로세스 리스트
    StackNode* next; // 다음 노드를 가리키는 포인터
    StackNode() : next(nullptr) { }
};

class DynamicQueue
{
    private:
    StackNode* top; // 스택의 맨 위 (포그라운드 프로세스)
    StackNode* bottom; // 스택의 맨 아래 (백그라운드 프로세스)
    mutex mtx; // 스레드 안전을 보장하기 위한 뮤텍스

    public:
    DynamicQueue() : top(nullptr), bottom(nullptr) { }

    // 프로세스를 큐에 추가
    void enqueue(Process p)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode** targetNode = p.isForeground ? &top : &bottom;

        if (*targetNode == nullptr)
        {
            *targetNode = new StackNode();
            if (p.isForeground && bottom == nullptr)
            {
                bottom = top;
            }
            else if (!p.isForeground && top == nullptr)
            {
                top = bottom;
            }
        }

        (*targetNode)->processList.push_back(p);
    }

    // 프로세스를 큐에서 제거
    Process dequeue()
    {
        lock_guard < mutex > lock (mtx) ;
        if (top && !top->processList.empty())
        {
            Process p = top->processList.front();
            top->processList.pop_front();
            if (top->processList.empty())
            {
                StackNode* temp = top;
                top = top->next;
                if (top == nullptr)
                {
                    bottom = nullptr;
                }
                delete temp;
            }
            return p;
        }
        return Process(-1, true); // 큐가 비어 있는 경우 더미 프로세스 반환
    }

    // 프로세스를 프로모션
    void promote()
    {
        lock_guard < mutex > lock (mtx) ;
        if (top && top->next)
        {
            StackNode* promotedNode = top;
            top = top->next;
            promotedNode->next = nullptr;
            if (promotedNode->processList.empty())
            {
                delete promotedNode;
            }
            else
            {
                StackNode* targetNode = top;
                while (targetNode->next)
                {
                    targetNode = targetNode->next;
                }
                targetNode->next = promotedNode;
            }
        }
    }

    // 프로세스를 분할하거나 병합
    void split_n_merge(int threshold)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = top;
        while (targetNode)
        {
            if (targetNode->processList.size() > threshold)
            {
                StackNode* newNode = new StackNode();
                auto it = targetNode->processList.begin();
                advance(it, threshold / 2);
                newNode->processList.splice(newNode->processList.begin(), targetNode->processList, targetNode->processList.begin(), it);
                newNode->next = targetNode->next;
                targetNode->next = newNode;
            }
            targetNode = targetNode->next;
        }
    }

    // 큐의 내용을 출력
    void printQueue()
    {
        lock_guard < mutex > lock (mtx) ;

        // Running 프로세스 출력
        cout << "Running: ";
        if (bottom && !bottom->processList.empty())
        {
            for (const auto&process : bottom->processList)
            {
                if (!process.isForeground)
                {
                    cout << "[" << process.pid << "B]";
                    break;
                }
            }
        }
        cout << endl;
        cout << "--------------------------" << endl;

        // DQ 출력
        cout << "DQ: ";
        StackNode* current = bottom;
        bool isBottom = true;
        while (current)
        {
            for (const auto&process : current->processList)
            {
                cout << "[" << process.pid << (process.isForeground ? "F" : "B") << "]";
                if (isBottom)
                {
                    cout << " (bottom)";
                    isBottom = false;
                }
            }
            current = current->next;
        }
        if (top == bottom)
        {
            cout << " (top)";
        }
        cout << endl;

        cout << " P => ";
        current = bottom;
        while (current)
        {
            for (const auto&process : current->processList)
            {
                if (!process.isForeground)
                {
                    cout << "[" << process.pid << "B]";
                    if (process.remainingTime > 0)
                    {
                        cout << " *" << process.remainingTime;
                    }
                    cout << " ";
                }
            }
            current = current->next;
        }
        cout << endl;

        cout << "--------------------------" << endl;

        // WQ 출력
        cout << "WQ: ";
        current = bottom;
        while (current)
        {
            for (const auto&process : current->processList)
            {
                if (!process.isForeground)
                {
                    cout << "[" << process.pid << "B:" << process.remainingTime << "]";
                }
            }
            current = current->next;
        }
        cout << endl << endl; // WQ와 Running 사이에 한 칸 띄우기
    }
};

// 프로세스 시뮬레이션 함수
void simulateProcesses(DynamicQueue& queue)
{
    srand(time(nullptr));
    int count = 0;
    while (count < 20)
    { // 20번 반복하여 시뮬레이션 실행
        // 프로세스 생성 시뮬레이션
        int pid = rand() % 100; // 임의의 프로세스 ID 생성
        bool isForeground = rand() % 2 == 0; // 프로세스가 포그라운드인지 백그라운드인지 무작위로 결정
        Process p(pid, isForeground);
        if (!isForeground)
            p.remainingTime = rand() % 10 + 1; // 백그라운드 프로세스의 임의의 남은 시간 설정

        // 프로세스 큐에 추가
        queue.enqueue(p);

        // 프로세스 프로모션 및 분할/병합
        queue.promote();
        queue.split_n_merge(5); // 5를 임의의 임계값으로 설정 (시연용)

        // 큐 출력
        queue.printQueue();

        // 실제 시간 경과 시뮬레이션
        this_thread::sleep_for(chrono::seconds(1));

        count++;
    }
}

int main()
{
    DynamicQueue queue;
    // 초기 상태로 0F와 1B 프로세스 추가
    queue.enqueue(Process(0, true));  // shell 프로세스
    queue.enqueue(Process(1, false)); // monitor 프로세스
    simulateProcesses(queue);
    return 0;
}
//2-2
#include <iostream>
#include <fstream>
#include <sstream>
#include <vector>
#include <thread>
#include <chrono>
#include <mutex>
#include <condition_variable>
#include <map>
#include <queue>
#include <functional>
#include <atomic>
#include <algorithm>
#include <numeric>
#include <climits>

using namespace std;

mutex cout_mutex; // 출력을 동기화하기 위한 뮤텍스
condition_variable cv; // 조건 변수
atomic<bool> done(false); // 프로그램 종료 여부를 나타내는 원자적 변수

// 안전하게 출력하는 함수
void safe_print(const string& str)
{
    lock_guard < mutex > lock (cout_mutex) ;
    cout << str << endl;
}

// 문자열 출력 함수
void echo(const string& str)
{
    safe_print(str);
}

// 더미 함수
void dummy() { }

// 최대공약수 계산 함수
int gcd(int x, int y)
{
    while (y != 0)
    {
        int temp = y;
        y = x % y;
        x = temp;
    }
    safe_print(to_string(x));
    return x;
}

// 소수 개수 세는 함수
int count_primes(int x)
{
    vector<bool> sieve(x +1, true);
    sieve[0] = sieve[1] = false;
    for (int i = 2; i * i <= x; ++i)
    {
        if (sieve[i])
        {
            for (int j = i * i; j <= x; j += i)
            {
                sieve[j] = false;
            }
        }
    }
    int prime_count = count(sieve.begin(), sieve.end(), true);
    safe_print(to_string(prime_count));
    return prime_count;
}

// 1부터 x까지의 합 계산 함수
int sum_upto(int x)
{
    int result = (x * (x + 1) / 2) % 1000000;
    safe_print(to_string(result));
    return result;
}

// 병렬로 1부터 x까지의 합 계산 함수
int sum_upto_parallel(int x, int parts)
{
    auto partial_sum = [](int start, int end)-> int {
        return (end * (end + 1) / 2 - (start - 1) * start / 2) % 1000000;
    };

    vector<thread> threads;
    vector<int> results(parts);
    int step = x / parts;

    for (int i = 0; i < parts; ++i)
    {
        int start = i * step + 1;
        int end = (i == parts - 1) ? x : (i + 1) * step;
        threads.emplace_back([&, i, start, end] {
            results[i] = partial_sum(start, end);
        });
}

    for (auto& t : threads)
    {
        t.join();
    }

    int result = accumulate(results.begin(), results.end(), 0) % 1000000;
safe_print(to_string(result));
return result;
}

// 사용자 명령 처리 함수
void handle_command(const string& command)
{
    stringstream ss(command);
    string cmd;
    vector<string> parts;
    while (ss >> cmd)
    {
        parts.push_back(cmd);
    }

    map<string, int> options = { { "-n", 1 }, { "-d", INT_MAX }, { "-p", 0 }, { "-m", 1 } };
    size_t i = 1;

    // 옵션 파싱
    while (i < parts.size())
    {
        if (options.count(parts[i]))
        {
            options[parts[i]] = stoi(parts[i + 1]);
            i += 2;
        }
        else
        {
            break;
        }
    }

    auto execute = [&]() {
        if (parts[0] == "echo") // "echo" 명령 처리
        {
            for (int j = 0; j < options["-n"]; ++j)
            {
                echo(parts[1]);
                if (options["-p"])
                {
                    this_thread::sleep_for(chrono::seconds(options["-p"]));
                }
            }
        }
        else if (parts[0] == "dummy") // "dummy" 명령 처리
        {
            for (int j = 0; j < options["-n"]; ++j)
            {
                dummy();
            }
        }
        else if (parts[0] == "gcd") // "gcd" 명령 처리
        {
            int x = stoi(parts[1]);
            int y = stoi(parts[2]);
            for (int j = 0; j < options["-n"]; ++j)
            {
                gcd(x, y);
            }
        }
        else if (parts[0] == "prime") // "prime" 명령 처리
        {
            int x = stoi(parts[1]);
            for (int j = 0; j < options["-n"]; ++j)
            {
                count_primes(x);
            }
        }
        else if (parts[0] == "sum") // "sum" 명령 처리
        {
            int x = stoi(parts[1]);
            for (int j = 0; j < options["-n"]; ++j)
            {
                sum_upto_parallel(x, options["-m"]);
            }
        }
    };

    if (options["-p"]) // 주기적 실행인 경우
    {
        thread([&]() {
            auto start_time = chrono::system_clock::now();
            while (chrono::duration_cast<chrono::seconds>(chrono::system_clock::now() - start_time).count() < options["-d"] && !done)
            {
                execute();
                this_thread::sleep_for(chrono::seconds(options["-p"]));
            }
        }).detach();
    }
    else // 주기적 실행이 아닌 경우
    {
        execute();
    }
}

// 명령 처리 함수
void process_commands(const vector<string>& commands, int interval)
{
    for (const auto&command : commands)
    {
        if (done) break;

stringstream ss(command);
string segment;
vector<string> fg_cmds;
vector<string> bg_cmds;

// 명령 분할
while (getline(ss, segment, ';'))
{
    if (segment.find("&") == 0)
    {
        bg_cmds.push_back(segment.substr(1));
    }
    else
    {
        fg_cmds.push_back(segment);
    }
}

// 포그라운드 명령 처리
for (const auto&cmd : fg_cmds)
        {
            handle_command(cmd);
        }

        vector<thread> bg_threads;
// 백그라운드 명령 처리
for (const auto&cmd : bg_cmds)
        {
            bg_threads.emplace_back(handle_command, cmd);
        }

        // 백그라운드 스레드 조인
        for (auto & t : bg_threads)
{
    if (t.joinable())
    {
        t.join();
    }
}

// 일정 시간 대기
this_thread::sleep_for(chrono::seconds(interval));
    }

    // 프로그램 종료 메시지 출력
    safe_print("All commands processed. Exiting...");
}

// 프로세스 모니터링 함수
void monitor()
{
    while (!done)
    {
        unique_lock < mutex > lock (cout_mutex) ;
        cv.wait_for(lock, chrono::seconds(5), [] { return done.load(); });
        if (!done)
        {
            cout << "Monitoring running processes..." << endl;
        }
    }
}

// 메인 함수
int main()
{
    ifstream file("commands.txt");
    if (!file.is_open())
    {
        cerr << "Failed to open commands.txt" << endl;
        return 1;
    }

    // 파일에서 명령 읽기
    string line;
    vector<string> commands;
    while (getline(file, line))
    {
        commands.push_back(line);
    }
    file.close();

    // 쉘 스레드 시작
    thread shell_thread(process_commands, commands, 1);
    // 모니터링 스레드 시작
    thread monitor_thread(monitor);

    // 쉘 스레드 조인
    shell_thread.join();
    // 프로그램 종료 상태 변경
    done = true;
    // 모니터링 스레드 조인
    cv.notify_all();
    monitor_thread.join();

    return 0;
}
//2-3