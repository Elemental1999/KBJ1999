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
    int pid;
    bool isForeground; // true면 foreground, false면 background
    int remainingTime; // 백그라운드 프로세스의 남은 시간
    bool isPromoted;   // 프로모션된 프로세스인 경우 true
    Process(int id, bool fg = true) : pid(id), isForeground(fg), remainingTime(0), isPromoted(false) { }
};

// 스택 노드 구조체
struct StackNode
{
    list<Process> processList;
    StackNode* next;
    StackNode() : next(nullptr) { }
};

// 대기 큐 구조체
struct WaitQueue
{
    list<Process> processList;
    mutex mtx;
};

class DynamicQueue
{
    private:
    StackNode* top;    // 스택의 맨 위 (foreground 프로세스)
    StackNode* bottom; // 스택의 맨 아래 (background 프로세스)
    mutex mtx;         // 스레드 안전성을 위한 뮤텍스
    WaitQueue wq;      // 대기 큐

    public:
    DynamicQueue() : top(nullptr), bottom(nullptr) { }

    // 프로세스 enqueue
    void enqueue(Process p)
    {
        lock_guard < mutex > lock (mtx) ;
        if (p.isForeground)
        {
            top = ensureNode(top);
            top->processList.push_back(p);
        }
        else
        {
            bottom = ensureNode(bottom);
            bottom->processList.push_back(p);
        }
    }

    // 프로세스 dequeue
    Process dequeue()
    {
        lock_guard < mutex > lock (mtx) ;
        if (top && !top->processList.empty())
        {
            Process p = top->processList.front();
            top->processList.pop_front();
            if (top->processList.empty())
            {
                StackNode* oldTop = top;
                top = top->next;
                delete oldTop;
            }
            return p;
        }
        return Process(-1, true); // 큐가 비어있는 경우 더미 프로세스 반환
    }

    // 프로세스 프로모션
    void promote()
    {
        lock_guard < mutex > lock (mtx) ;
        if (bottom && !bottom->processList.empty())
        {
            Process promotedProcess = bottom->processList.front();
            bottom->processList.pop_front();
            promotedProcess.isPromoted = true;
            top = ensureNode(top);
            top->processList.push_back(promotedProcess);
        }
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

    // 프로세스 유형(foreground/background)에 대한 노드의 존재 여부 확인
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

        // Running 상태 출력
        ss << "Running: ";
        if (top && !top->processList.empty())
        {
            auto process = top->processList.front();
            ss << "[" << process.pid << (process.isForeground ? "F" : "B") << "]";
        }
        else
        {
            ss << "[]";
        }
        ss << "\n--------------------------\n";

        // Dynamic Queue 출력
        ss << "DQ: ";
        StackNode* current = top;
        while (current)
        {
            for (const auto&process : current->processList) {
                ss << "[" << process.pid << (process.isForeground ? "F" : "B");
                if (process.isPromoted)
                {
                    ss << "*";
                }
                ss << "] ";
            }
            if (current->next)
            {
                ss << "-> ";
            }
            else
            {
                ss << "(bottom/top)";
            }
            current = current->next;
        }
        ss << "\n--------------------------\n";

        // Wait Queue 출력
        ss << "WQ: ";
        lock_guard<mutex> wqLock(wq.mtx);
        for (const auto&process : wq.processList) {
            ss << "[" << process.pid << (process.isForeground ? "F" : "B") << ":" << process.remainingTime << "] ";
        }
        ss << "\n--------------------------\n";

        cout << ss.str();
    }

    // 대기 큐에 프로세스 추가
    void addToWaitQueue(Process p)
    {
        lock_guard < mutex > lock (wq.mtx) ;
        wq.processList.push_back(p);
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
        if (!isForeground)
            p.remainingTime = rand() % 10 + 1; // 백그라운드 프로세스의 잔여 시간 랜덤 생성

        // 프로세스 enqueue
        queue.enqueue(p);

        // 프로세스 프로모션 및 분할/병합
        queue.promote();
        queue.split_n_merge(5); // 데모를 위해 임계값을 5로 설정

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
        StackNode* targetNode = p.isForeground ? top : bottom;
        targetNode = ensureNode(targetNode);
        targetNode->processList.push_back(p);
    }

    // 프로세스를 큐에서 제거
    Process dequeue()
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = top;
        if (top && !top->processList.empty())
        {
            Process p = top->processList.front();
            top->processList.pop_front();
            if (top->processList.empty())
            {
                top = top->next;
                delete targetNode;
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

    // 주어진 프로세스 유형 (포그라운드/백그라운드)에 대한 노드의 존재 여부 확인
    StackNode* ensureNode(StackNode* node)
    {
        if (!node)
        {
            node = new StackNode();
            if (node->processList.empty())
            {
                if (node->next == nullptr)
                {
                    if (node->processList.empty())
                        top = bottom = node;
                }
                else if (node->processList.empty())
                    top = node;
            }
            else
            {
                if (node->next == nullptr)
                {
                    if (node->processList.empty())
                        bottom = node;
                }
            }
        }
        return node;
    }

    // 큐의 내용을 출력
    void printQueue()
    {
        lock_guard < mutex > lock (mtx) ;

        // Running 프로세스 출력
        cout << "Running: ";
        StackNode* current = bottom;
        while (current)
        {
            for (const auto&process : current->processList)
            {
                cout << "[" << process.pid << (process.isForeground ? "F" : "B") << "]";
            }
            cout << "-";
            current = current->next;
        }
        cout << endl;

        // DQ 출력
        cout << "DQ: ";
        current = bottom;
        while (current)
        {
            for (const auto&process : current->processList)
            {
                cout << "[" << process.pid << (process.isForeground ? "F" : "B") << "]";
            }
            if (current == top)
            {
                cout << " (bottom/top)";
            }
            cout << "-";
            current = current->next;
        }
        cout << endl;

        // P 출력
        cout << " P => ";
        current = bottom;
        while (current)
        {
            for (const auto&process : current->processList)
            {
                if (!process.isForeground)
                {
                    cout << "[" << process.pid << (process.isForeground ? "F" : "B") << "]";
                    if (process.remainingTime > 0)
                    {
                        cout << " *" << process.remainingTime << " ";
                    }
                    else
                    {
                        cout << " ";
                    }
                }
            }
            current = current->next;
        }
        cout << endl;

        // WQ 출력
        cout << "WQ: ";
        current = bottom;
        while (current)
        {
            for (const auto&process : current->processList)
            {
                if (!process.isForeground)
                {
                    cout << "[" << process.pid << (process.isForeground ? "F" : "B") << "]";
                    cout << ":" << process.remainingTime << " ";
                }
            }
            current = current->next;
        }
        cout << endl;
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

// Parse 함수: 명령을 입력 받아 토큰으로 파싱하여 반환
char** parse(const char* command)
{
    char** args = new char*[64]; // 최대 64개의 토큰을 저장할 수 있는 배열
    char* temp = strdup(command); // 입력 명령을 복제하여 임시로 저장

    int i = 0;
    char* token = strtok(temp, " "); // 공백을 기준으로 첫 번째 토큰 추출
    while (token != nullptr)
    {
        args[i++] = token; // 추출한 토큰을 배열에 저장
        token = strtok(nullptr, " "); // 다음 토큰 추출
    }
    args[i] = nullptr; // 배열의 끝을 나타내는 nullptr 추가

    delete[] temp; // 복제한 문자열 메모리 해제
    return args;
}

// Exec 함수: 명령어를 실행
void exec(char** args)
{
    pid_t pid = fork(); // 새로운 프로세스 생성
    if (pid == -1)
    {
        perror("fork failed");
        exit(EXIT_FAILURE);
    }
    else if (pid == 0)
    {
        // 자식 프로세스일 경우
        if (execvp(args[0], args) == -1)
        {
            perror("execvp failed");
            exit(EXIT_FAILURE);
        }
    }
    else
    {
        // 부모 프로세스일 경우
        wait(nullptr); // 자식 프로세스의 종료를 기다림
    }
}

int main()
{
    DynamicQueue queue;
    simulateProcesses(queue);
    return 0;
}//2-2
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

mutex cout_mutex;
condition_variable cv;
atomic<bool> done(false);
queue<string> commandQueue;

void echo(const string& str)
{
    lock_guard < mutex > lock (cout_mutex) ;
    cout << str << endl;
}

void dummy() { }

int gcd(int x, int y)
{
    while (y != 0)
    {
        int temp = y;
        y = x % y;
        x = temp;
    }
    lock_guard < mutex > lock (cout_mutex) ;
    cout << x << endl;
    return x;
}

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
    lock_guard < mutex > lock (cout_mutex) ;
    cout << prime_count << endl;
    return prime_count;
}

int sum_upto(int x)
{
    int result = (x * (x + 1) / 2) % 1000000;
    lock_guard < mutex > lock (cout_mutex) ;
    cout << result << endl;
    return result;
}

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

    for (auto& t : threads) {
        t.join();
    }

    int result = accumulate(results.begin(), results.end(), 0) % 1000000;
lock_guard < mutex > lock (cout_mutex) ;
cout << result << endl;
return result;
}

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
        if (parts[0] == "echo")
        {
            for (int j = 0; j < options["-n"]; ++j)
            {
                if (options["-p"])
                {
                    while (true)
                    {
                        echo(parts[1]);
                        this_thread::sleep_for(chrono::seconds(options["-p"]));
                    }
                }
                else
                {
                    echo(parts[1]);
                }
            }
        }
        else if (parts[0] == "dummy")
        {
            for (int j = 0; j < options["-n"]; ++j)
            {
                dummy();
            }
        }
        else if (parts[0] == "gcd")
        {
            int x = stoi(parts[1]);
            int y = stoi(parts[2]);
            for (int j = 0; j < options["-n"]; ++j)
            {
                gcd(x, y);
            }
        }
        else if (parts[0] == "prime")
        {
            int x = stoi(parts[1]);
            for (int j = 0; j < options["-n"]; ++j)
            {
                count_primes(x);
            }
        }
        else if (parts[0] == "sum")
        {
            int x = stoi(parts[1]);
            for (int j = 0; j < options["-n"]; ++j)
            {
                sum_upto_parallel(x, options["-m"]);
            }
        }
    };

    if (options["-p"])
    {
        auto periodic_execution = [&]() {
            auto start_time = chrono::system_clock::now();
            while (chrono::duration_cast<chrono::seconds>(chrono::system_clock::now() - start_time).count() < options["-d"])
            {
                execute();
                this_thread::sleep_for(chrono::seconds(options["-p"]));
            }
        };
        thread(periodic_execution).detach();
    }
    else
    {
        execute();
    }
}

void process_commands(const vector<string>& commands, int interval)
{
    for (const auto&command : commands) {
        if (done) break;

stringstream ss(command);
string segment;
vector<string> fg_cmds;
vector<string> bg_cmds;

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

for (const auto&cmd : fg_cmds) {
            handle_command(cmd);
        }

        vector<thread> bg_threads;
for (const auto&cmd : bg_cmds) {
            bg_threads.emplace_back(handle_command, cmd);
        }

        for (auto & t : bg_threads)
{
    if (t.joinable())
    {
        t.join();
    }
}

this_thread::sleep_for(chrono::seconds(interval));
    }

    lock_guard < mutex > lock (cout_mutex) ;
cout << "All commands processed. Exiting..." << endl;
}

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

int main()
{
    ifstream file("commands.txt");
    if (!file.is_open())
    {
        cerr << "Failed to open commands.txt" << endl;
        return 1;
    }

    string line;
    vector<string> commands;
    while (getline(file, line))
    {
        commands.push_back(line);
    }
    file.close();

    thread shell_thread(process_commands, commands, 1);
    thread monitor_thread(monitor);

    shell_thread.join();
    done = true;
    cv.notify_all();
    monitor_thread.join();

    return 0;
}
//2-3